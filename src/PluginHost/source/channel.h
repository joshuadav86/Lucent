// The pipe to Lucent: the host's original standard input and output, kept for itself so plugins that
// print can't garble it. See protocol.h for what goes over it.

#pragma once

#include <cstdint>
#include <cstring>
#include <stdexcept>
#include <string>
#include <vector>

namespace Lucent {

/// Takes over standard input and output for talking to Lucent, and points the process's own
/// stdin/stdout at nothing, so anything a plugin prints is thrown away. Call before loading plugins.
void claimStandardStreams ();

/// Reads exactly `size` bytes from Lucent. Returns false if Lucent has gone (the pipe closed).
bool readFromLucent (void* data, size_t size);

/// Writes all of `data` to Lucent. Returns false if Lucent has gone.
bool writeToLucent (const void* data, size_t size);

/// Builds a message payload.
class Writer
{
public:
	template <typename T>
	void put (T value)
	{
		auto at = bytes.size ();
		bytes.resize (at + sizeof (T));
		std::memcpy (bytes.data () + at, &value, sizeof (T));
	}

	void putString (const std::string& text)
	{
		put<int32_t> (static_cast<int32_t> (text.size ()));
		putBytes (text.data (), text.size ());
	}

	void putBlob (const std::vector<char>& blob)
	{
		put<int32_t> (static_cast<int32_t> (blob.size ()));
		putBytes (blob.data (), blob.size ());
	}

	void putBytes (const void* data, size_t size)
	{
		auto at = bytes.size ();
		bytes.resize (at + size);
		if (size > 0)
			std::memcpy (bytes.data () + at, data, size);
	}

	std::vector<char> bytes;
};

/// Reads a message payload, throwing if it's shorter than expected.
class Reader
{
public:
	explicit Reader (const std::vector<char>& bytes) : bytes (bytes) {}

	template <typename T>
	T get ()
	{
		T value;
		std::memcpy (&value, take (sizeof (T)), sizeof (T));
		return value;
	}

	std::string getString ()
	{
		auto length = get<int32_t> ();
		if (length < 0)
			throw std::runtime_error ("Bad message from Lucent.");
		auto data = take (static_cast<size_t> (length));
		return std::string (data, data + length);
	}

	std::vector<char> getBlob ()
	{
		auto length = get<int32_t> ();
		if (length < 0)
			throw std::runtime_error ("Bad message from Lucent.");
		auto data = take (static_cast<size_t> (length));
		return std::vector<char> (data, data + length);
	}

	const char* take (size_t size)
	{
		if (size > bytes.size () - position)
			throw std::runtime_error ("Bad message from Lucent.");
		auto data = bytes.data () + position;
		position += size;
		return data;
	}

private:
	const std::vector<char>& bytes;
	size_t position {0};
};

} // namespace Lucent
