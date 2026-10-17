#include "channel.h"

#if defined(_WIN32)
#include <windows.h>
#include <cstdio>
#else
#include <fcntl.h>
#include <unistd.h>
#endif

namespace Lucent {

#if defined(_WIN32)

static HANDLE input = INVALID_HANDLE_VALUE;
static HANDLE output = INVALID_HANDLE_VALUE;

void claimStandardStreams ()
{
	auto self = GetCurrentProcess ();
	DuplicateHandle (self, GetStdHandle (STD_INPUT_HANDLE), self, &input, 0, FALSE, DUPLICATE_SAME_ACCESS);
	DuplicateHandle (self, GetStdHandle (STD_OUTPUT_HANDLE), self, &output, 0, FALSE, DUPLICATE_SAME_ACCESS);

	// From here on, standard input reads nothing and standard output goes nowhere, both for this
	// process and for plugins (their C runtimes pick these up when they load).
	FILE* ignored = nullptr;
	freopen_s (&ignored, "NUL", "r", stdin);
	freopen_s (&ignored, "NUL", "w", stdout);
	auto nulIn = CreateFileW (L"NUL", GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_EXISTING, 0, nullptr);
	auto nulOut = CreateFileW (L"NUL", GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, nullptr, OPEN_EXISTING, 0, nullptr);
	SetStdHandle (STD_INPUT_HANDLE, nulIn);
	SetStdHandle (STD_OUTPUT_HANDLE, nulOut);
}

bool readFromLucent (void* data, size_t size)
{
	auto at = static_cast<char*> (data);
	while (size > 0)
	{
		DWORD read = 0;
		auto chunk = static_cast<DWORD> (size > (1u << 30) ? (1u << 30) : size);
		if (!ReadFile (input, at, chunk, &read, nullptr) || read == 0)
			return false;
		at += read;
		size -= read;
	}
	return true;
}

bool writeToLucent (const void* data, size_t size)
{
	auto at = static_cast<const char*> (data);
	while (size > 0)
	{
		DWORD written = 0;
		auto chunk = static_cast<DWORD> (size > (1u << 30) ? (1u << 30) : size);
		if (!WriteFile (output, at, chunk, &written, nullptr) || written == 0)
			return false;
		at += written;
		size -= written;
	}
	return true;
}

#else

static int input = -1;
static int output = -1;

void claimStandardStreams ()
{
	input = dup (STDIN_FILENO);
	output = dup (STDOUT_FILENO);
	auto nothing = open ("/dev/null", O_RDWR);
	dup2 (nothing, STDIN_FILENO);
	dup2 (nothing, STDOUT_FILENO);
	close (nothing);
}

bool readFromLucent (void* data, size_t size)
{
	auto at = static_cast<char*> (data);
	while (size > 0)
	{
		auto count = read (input, at, size);
		if (count <= 0)
			return false;
		at += count;
		size -= static_cast<size_t> (count);
	}
	return true;
}

bool writeToLucent (const void* data, size_t size)
{
	auto at = static_cast<const char*> (data);
	while (size > 0)
	{
		auto count = write (output, at, size);
		if (count <= 0)
			return false;
		at += count;
		size -= static_cast<size_t> (count);
	}
	return true;
}

#endif

} // namespace Lucent
