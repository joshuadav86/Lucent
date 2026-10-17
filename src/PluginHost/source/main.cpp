// LucentPluginHost: runs one VST3 effect for Lucent's audio editor, in a process of its own so a
// plugin that crashes can't take Lucent down with it. See protocol.h for how Lucent drives it.

#include "channel.h"
#include "plugin.h"
#include "protocol.h"
#include "ui.h"

#include "public.sdk/source/vst/hosting/module.h"
#include "pluginterfaces/vst/ivstaudioprocessor.h"

#include <cstring>
#include <exception>
#include <memory>
#include <string>
#include <thread>
#include <vector>

#if defined(_WIN32)
#include <windows.h>
#include <shellapi.h>
#else
#include <csignal>
#endif

namespace Lucent {

namespace {

/// Tabs and line breaks would split the scan output's fields and lines.
std::string field (std::string text)
{
	for (auto& c : text)
	{
		if (c == '\t' || c == '\r' || c == '\n')
			c = ' ';
	}
	return text;
}

void writeLine (const std::string& line)
{
	auto text = line + "\n";
	writeToLucent (text.data (), text.size ());
}

/// Lists the effects in one .vst3 file.
int scan (const std::string& path)
{
	std::string error;
	auto module = VST3::Hosting::Module::create (path, error);
	if (!module)
	{
		writeLine ("ERROR\t" + field (error.empty () ? "It couldn't be loaded." : error));
		return 0;
	}

	for (auto& info : module->getFactory ().classInfos ())
	{
		if (info.category () != kVstAudioEffectClass)
			continue;

		// Instruments make sound rather than change it, so they're no use in the editor.
		auto categories = info.subCategoriesString ();
		if (categories.find ("Instrument") != std::string::npos && categories.find ("Fx") == std::string::npos)
			continue;

		writeLine ("PLUGIN\t" + info.ID ().toString () + "\t" + field (info.name ()) + "\t" + field (info.vendor ()) + "\t" +
		           field (info.version ()) + "\t" + field (categories));
	}
	return 0;
}

bool reply (Status status, const std::vector<char>& payload)
{
	uint32_t header[2] = {static_cast<uint32_t> (status), static_cast<uint32_t> (payload.size ())};
	return writeToLucent (header, sizeof (header)) && writeToLucent (payload.data (), payload.size ());
}

/// Answers Lucent's requests until it says goodbye or goes away.
void serve ()
{
	auto plugin = std::make_unique<Plugin> ();
	bool loaded = false;
	std::vector<char> payload;
	std::vector<float> audio;

	while (true)
	{
		uint32_t header[2];
		if (!readFromLucent (header, sizeof (header)))
			break;
		auto command = static_cast<Command> (header[0]);
		payload.resize (header[1]);
		if (!readFromLucent (payload.data (), payload.size ()))
			break;
		if (command == Command::Quit)
		{
			reply (Status::Done, {});
			break;
		}

		Writer answer;
		try
		{
			Reader request (payload);
			if (command != Command::Load && !loaded)
				throw std::runtime_error ("No plugin is loaded.");

			switch (command)
			{
				case Command::Load:
				{
					if (loaded)
						throw std::runtime_error ("A plugin is loaded already.");
					auto path = request.getString ();
					auto classId = request.getString ();
					auto sampleRate = request.get<int32_t> ();
					auto channels = request.get<int32_t> ();
					runOnUiThread ([&] {
						try
						{
							plugin->load (path, classId, sampleRate, channels);
						}
						catch (...)
						{
							plugin->unload ();
							throw;
						}
					});
					loaded = true;
					answer.putString (plugin->name ());
					answer.put<int32_t> (plugin->latency ());
					answer.put<int64_t> (plugin->tail ());
					answer.put<uint8_t> (plugin->controller () ? 1 : 0);
					break;
				}
				case Command::Start:
				{
					auto offline = request.get<int32_t> () == 1;
					runOnUiThread ([&] { plugin->start (offline); });
					answer.put<int32_t> (plugin->latency ());
					answer.put<int64_t> (plugin->tail ());
					break;
				}
				case Command::Process:
				{
					auto frames = request.get<int32_t> ();
					auto channels = plugin->channels ();
					if (frames < 0 || frames > (1 << 22))
						throw std::runtime_error ("Bad message from Lucent.");
					auto count = static_cast<size_t> (frames) * static_cast<size_t> (channels);
					audio.resize (count);
					std::memcpy (audio.data (), request.take (count * sizeof (float)), count * sizeof (float));
					float* planes[2] = {audio.data (), audio.data () + (channels > 1 ? frames : 0)};
					plugin->process (planes, frames);
					answer.putBytes (audio.data (), count * sizeof (float));
					break;
				}
				case Command::ShowEditor:
				{
					auto owner = request.get<int64_t> ();
					auto title = request.getString ();
					runOnUiThread ([&] { showEditor (*plugin, owner, title); });
					break;
				}
				case Command::HideEditor:
					runOnUiThread ([] { hideEditor (); });
					break;
				case Command::GetState:
				{
					std::vector<char> component, controller;
					runOnUiThread ([&] {
						component = plugin->componentState ();
						controller = plugin->controllerState ();
					});
					answer.putBlob (component);
					answer.putBlob (controller);
					break;
				}
				case Command::SetState:
				{
					auto component = request.getBlob ();
					auto controller = request.getBlob ();
					runOnUiThread ([&] { plugin->setState (component, controller); });
					break;
				}
				default:
					throw std::runtime_error ("Lucent asked for something this plugin host doesn't know.");
			}
		}
		catch (const std::exception& error)
		{
			Writer message;
			message.putBytes (error.what (), std::strlen (error.what ()));
			if (!reply (Status::Failed, message.bytes))
				break;
			continue;
		}
		catch (...)
		{
			std::string text = "The plugin ran into a problem.";
			if (!reply (Status::Failed, std::vector<char> (text.begin (), text.end ())))
				break;
			continue;
		}

		if (!reply (Status::Done, answer.bytes))
			break;
	}

	// Plugins are shut down on the thread they were started on.
	try
	{
		runOnUiThread ([&] {
			closeEditor ();
			plugin.reset ();
		});
	}
	catch (...)
	{
	}
	stopUiLoop ();
}

} // namespace

int run (const std::vector<std::string>& args)
{
	claimStandardStreams ();
#if !defined(_WIN32)
	// A write to Lucent after it has gone fails instead of ending the process mid-cleanup.
	std::signal (SIGPIPE, SIG_IGN);
#endif

	if (args.size () >= 2 && args[0] == "--scan")
		return scan (args[1]);
	if (args.empty () || args[0] != "--session")
		return 2;

	prepareUiThread ();
	std::thread server (serve);
	runUiLoop ();
	server.join ();
	return 0;
}

} // namespace Lucent

#if defined(_WIN32)

int WINAPI wWinMain (HINSTANCE, HINSTANCE, PWSTR, int)
{
	int count = 0;
	auto wide = CommandLineToArgvW (GetCommandLineW (), &count);
	std::vector<std::string> args;
	for (int i = 1; i < count; ++i)
	{
		auto size = WideCharToMultiByte (CP_UTF8, 0, wide[i], -1, nullptr, 0, nullptr, nullptr);
		std::string arg (static_cast<size_t> (size > 0 ? size - 1 : 0), '\0');
		if (size > 1)
			WideCharToMultiByte (CP_UTF8, 0, wide[i], -1, arg.data (), size, nullptr, nullptr);
		args.push_back (arg);
	}
	LocalFree (wide);
	return Lucent::run (args);
}

#else

int main (int argc, char* argv[])
{
	return Lucent::run (std::vector<std::string> (argv + 1, argv + argc));
}

#endif
