// How Lucent and LucentPluginHost talk to each other.
//
// Lucent starts the host with "--session" and sends requests on its standard input; the host answers
// each one, in order, on its standard output. A request is
//     uint32 command, uint32 payload length, payload
// and the answer is
//     uint32 status (0 = done, 1 = failed), uint32 payload length, payload
// where a failed answer's payload is a message for the user. Numbers are little-endian; a string is an
// int32 byte count followed by UTF-8. Audio is 32-bit float, one channel after another (planar).
//
// "--scan <path>" instead writes one line per effect found in a .vst3 file and exits:
//     PLUGIN \t class id \t name \t vendor \t version \t sub-categories
// or a single "ERROR \t message" line if the file can't be loaded.
//
// Keep this in step with PluginHost.cs in Lucent.

#pragma once

#include <cstdint>

namespace Lucent {

enum class Command : uint32_t
{
	// string path, string class id, int32 sample rate, int32 channels
	// -> string name, int32 latency, int64 tail (-1 = endless), uint8 has a window
	Load = 1,
	// int32 mode (0 = real time, for previews; 1 = offline, for rendering). Starts again from silence.
	// -> int32 latency, int64 tail
	Start = 2,
	// int32 frames, then the audio -> the processed audio, the same length and channels
	Process = 3,
	// int64 window to sit above (0 for none), string title -> nothing
	ShowEditor = 4,
	HideEditor = 5,
	// -> int32 length, component state, int32 length, controller state
	GetState = 6,
	// the same as GetState's answer -> nothing
	SetState = 7,
	Quit = 8,
};

enum class Status : uint32_t
{
	Done = 0,
	Failed = 1,
};

} // namespace Lucent
