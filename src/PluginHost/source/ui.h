// The host's main thread, where plugins expect to be set up and to draw their windows.

#pragma once

#include <cstdint>
#include <functional>
#include <string>

namespace Lucent {

class Plugin;

/// Gets the main thread ready for plugins and windows. Call on the main thread before anything else.
void prepareUiThread ();

/// Runs the main thread's loop (and plugin windows) until stopUiLoop is called.
void runUiLoop ();

/// Ends runUiLoop. Can be called from any thread.
void stopUiLoop ();

/// Runs `work` on the main thread and waits for it to finish. Exceptions are passed back.
void runOnUiThread (const std::function<void ()>& work);

/// Opens the plugin's own window, or brings it to the front if it's open already. `owner` is a
/// window of Lucent's for it to sit above (0 for none). Main thread only; throws with a message.
void showEditor (Plugin& plugin, int64_t owner, const std::string& title);

/// Hides the plugin's window, keeping it ready to show again. Main thread only.
void hideEditor ();

/// Closes the plugin's window for good, before the plugin is unloaded. Main thread only.
void closeEditor ();

} // namespace Lucent
