// The main thread on systems where plugin windows aren't supported yet: a plain work queue.

#include "ui.h"

#include <condition_variable>
#include <deque>
#include <exception>
#include <mutex>
#include <stdexcept>

namespace Lucent {

namespace {

std::mutex lock;
std::condition_variable wake;
std::deque<std::function<void ()>> queue;
bool stopping = false;

} // namespace

void prepareUiThread () {}

void runUiLoop ()
{
	while (true)
	{
		std::function<void ()> work;
		{
			std::unique_lock<std::mutex> guard (lock);
			wake.wait (guard, [] { return stopping || !queue.empty (); });
			if (queue.empty ())
				return;
			work = std::move (queue.front ());
			queue.pop_front ();
		}
		work ();
	}
}

void stopUiLoop ()
{
	std::lock_guard<std::mutex> guard (lock);
	stopping = true;
	wake.notify_all ();
}

void runOnUiThread (const std::function<void ()>& work)
{
	std::mutex doneLock;
	std::condition_variable doneWake;
	bool done = false;
	std::exception_ptr error;
	{
		std::lock_guard<std::mutex> guard (lock);
		if (stopping)
			throw std::runtime_error ("The plugin host is closing.");
		queue.push_back ([&] {
			try
			{
				work ();
			}
			catch (...)
			{
				error = std::current_exception ();
			}
			std::lock_guard<std::mutex> doneGuard (doneLock);
			done = true;
			doneWake.notify_all ();
		});
		wake.notify_all ();
	}
	std::unique_lock<std::mutex> doneGuard (doneLock);
	doneWake.wait (doneGuard, [&] { return done; });
	if (error)
		std::rethrow_exception (error);
}

void showEditor (Plugin&, int64_t, const std::string&)
{
	throw std::runtime_error ("Plugin windows aren't supported on this system yet.");
}

void hideEditor () {}

void closeEditor () {}

} // namespace Lucent
