#include "plugin.h"

#include "public.sdk/source/vst/hosting/hostclasses.h"
#include "public.sdk/source/vst/utility/stringconvert.h"
#include "pluginterfaces/base/funknownimpl.h"
#include "pluginterfaces/base/ibstream.h"
#include "pluginterfaces/vst/ivstprocesscontext.h"
#include "pluginterfaces/vst/vstspeaker.h"

#include <algorithm>
#include <cstring>
#include <stdexcept>

using namespace Steinberg;
using namespace Steinberg::Vst;

namespace Lucent {

namespace {

/// What plugins see as their host: it gives Lucent's name and makes the messages plugins pass between
/// their halves.
class LucentHostApplication : public HostApplication
{
public:
	tresult PLUGIN_API getName (String128 name) override
	{
		return StringConvert::convert ("Lucent", name) ? kResultTrue : kInternalError;
	}
};

LucentHostApplication& hostApplication ()
{
	static LucentHostApplication instance;
	return instance;
}

/// A growable block of memory that plugins save their settings into and read them back from.
class ByteStream : public U::Implements<U::Directly<IBStream>>
{
public:
	ByteStream () = default;
	explicit ByteStream (const std::vector<char>& bytes) : bytes (bytes) {}

	tresult PLUGIN_API read (void* buffer, int32 numBytes, int32* numBytesRead) override
	{
		auto available = static_cast<int64> (bytes.size ()) - position;
		auto count = static_cast<int32> (std::max<int64> (0, std::min<int64> (numBytes, available)));
		if (count > 0)
			std::memcpy (buffer, bytes.data () + position, static_cast<size_t> (count));
		position += count;
		if (numBytesRead)
			*numBytesRead = count;
		return kResultTrue;
	}

	tresult PLUGIN_API write (void* buffer, int32 numBytes, int32* numBytesWritten) override
	{
		if (numBytes < 0)
			return kInvalidArgument;
		auto end = static_cast<size_t> (position) + static_cast<size_t> (numBytes);
		if (end > bytes.size ())
			bytes.resize (end);
		if (numBytes > 0)
			std::memcpy (bytes.data () + position, buffer, static_cast<size_t> (numBytes));
		position += numBytes;
		if (numBytesWritten)
			*numBytesWritten = numBytes;
		return kResultTrue;
	}

	tresult PLUGIN_API seek (int64 pos, int32 mode, int64* result) override
	{
		int64 base = mode == kIBSeekSet ? 0 : mode == kIBSeekCur ? position : static_cast<int64> (bytes.size ());
		auto target = base + pos;
		if (target < 0)
			return kInvalidArgument;
		position = target;
		if (result)
			*result = position;
		return kResultTrue;
	}

	tresult PLUGIN_API tell (int64* pos) override
	{
		if (!pos)
			return kInvalidArgument;
		*pos = position;
		return kResultTrue;
	}

	std::vector<char> bytes;
	int64 position {0};
};

SpeakerArrangement arrangementFor (int channels)
{
	return channels == 1 ? SpeakerArr::kMono : SpeakerArr::kStereo;
}

[[noreturn]] void fail (const std::string& message)
{
	throw std::runtime_error (message);
}

} // namespace

// ---- Knob turns from the plugin's window ----

tresult PLUGIN_API ComponentHandler::performEdit (ParamID id, ParamValue value)
{
	std::lock_guard<std::mutex> guard (lock);
	for (auto& change : changes)
	{
		if (change.id == id)
		{
			change.value = value;
			return kResultOk;
		}
	}
	changes.push_back ({id, value});
	return kResultOk;
}

tresult PLUGIN_API ComponentHandler::restartComponent (int32 flags)
{
	if (flags & kLatencyChanged)
		latencyChanged = true;
	return kResultOk;
}

tresult PLUGIN_API ComponentHandler::queryInterface (const TUID iid, void** obj)
{
	if (FUnknownPrivate::iidEqual (iid, IComponentHandler::iid) || FUnknownPrivate::iidEqual (iid, FUnknown::iid))
	{
		*obj = static_cast<IComponentHandler*> (this);
		return kResultOk;
	}
	*obj = nullptr;
	return kNoInterface;
}

void ComponentHandler::takeChanges (ParameterChanges& into)
{
	std::lock_guard<std::mutex> guard (lock);
	for (auto& change : changes)
	{
		int32 index = 0;
		if (auto queue = into.addParameterData (change.id, index))
		{
			int32 point = 0;
			queue->addPoint (0, change.value, point);
		}
	}
	changes.clear ();
}

void ComponentHandler::forgetChanges ()
{
	std::lock_guard<std::mutex> guard (lock);
	changes.clear ();
}

// ---- Loading ----

Plugin::~Plugin ()
{
	unload ();
}

void Plugin::load (const std::string& path, const std::string& classId, double sampleRate, int channels)
{
	if (channels < 1 || channels > 2 || sampleRate < 1000)
		fail ("Lucent asked for audio the plugin host can't handle.");

	std::string error;
	module = VST3::Hosting::Module::create (path, error);
	if (!module)
		fail ("The plugin couldn't be loaded. " + error);

	auto factory = module->getFactory ();
	factory.setHostContext (static_cast<IHostApplication*> (&hostApplication ()));
	auto wanted = VST3::UID::fromString (classId);
	if (!wanted)
		fail ("Lucent asked for a plugin with a bad id.");

	const VST3::Hosting::ClassInfo* found = nullptr;
	auto classes = factory.classInfos ();
	for (auto& info : classes)
	{
		if (info.ID () == *wanted && info.category () == kVstAudioEffectClass)
			found = &info;
	}
	if (!found)
		fail ("This plugin file no longer has that effect in it. Look for plugins again and pick it from the list.");
	pluginName = found->name ();

	component = factory.createInstance<IComponent> (found->ID ());
	if (!component || component->initialize (static_cast<IHostApplication*> (&hostApplication ())) != kResultOk)
	{
		component = nullptr;
		fail (pluginName + " didn't start up.");
	}

	// Most plugins have a separate controller (settings and window) next to the component (audio);
	// a few are one object that does both.
	IEditController* combined = nullptr;
	if (component->queryInterface (IEditController::iid, reinterpret_cast<void**> (&combined)) == kResultTrue)
	{
		editController = owned (combined);
	}
	else
	{
		TUID controllerId;
		if (component->getControllerClassId (controllerId) == kResultTrue)
		{
			editController = factory.createInstance<IEditController> (VST3::UID (controllerId));
			if (editController)
			{
				if (editController->initialize (static_cast<IHostApplication*> (&hostApplication ())) == kResultOk)
					separateController = true;
				else
					editController = nullptr;
			}
		}
	}

	if (separateController)
	{
		componentConnection = U::cast<IConnectionPoint> (component);
		controllerConnection = U::cast<IConnectionPoint> (editController);
		if (componentConnection && controllerConnection)
		{
			componentConnection->connect (controllerConnection);
			controllerConnection->connect (componentConnection);
		}
	}

	if (editController)
	{
		editController->setComponentHandler (&handler);
		// The controller starts out showing the component's settings.
		ByteStream state;
		if (component->getState (&state) == kResultTrue)
		{
			state.position = 0;
			editController->setComponentState (&state);
		}
	}

	processor = U::cast<IAudioProcessor> (component);
	if (!processor)
		fail (pluginName + " doesn't process audio.");
	if (processor->canProcessSampleSize (kSample32) != kResultTrue)
		fail (pluginName + " can't process 32-bit audio.");

	auto inputBuses = component->getBusCount (kAudio, kInput);
	auto outputBuses = component->getBusCount (kAudio, kOutput);
	if (inputBuses < 1 || outputBuses < 1)
		fail (pluginName + " doesn't take audio in. It may be an instrument rather than an effect.");

	// Ask for the song's channels on the main buses, keeping whatever the others (side-chains) are.
	auto tryArrangement = [&] (int wantedChannels) {
		std::vector<SpeakerArrangement> inputs (static_cast<size_t> (inputBuses));
		std::vector<SpeakerArrangement> outputs (static_cast<size_t> (outputBuses));
		for (int32 i = 0; i < inputBuses; ++i)
			processor->getBusArrangement (kInput, i, inputs[static_cast<size_t> (i)]);
		for (int32 i = 0; i < outputBuses; ++i)
			processor->getBusArrangement (kOutput, i, outputs[static_cast<size_t> (i)]);
		inputs[0] = arrangementFor (wantedChannels);
		outputs[0] = arrangementFor (wantedChannels);
		return processor->setBusArrangements (inputs.data (), inputBuses, outputs.data (), outputBuses) == kResultTrue;
	};
	if (!tryArrangement (channels) && channels == 1)
		tryArrangement (2);

	// Whatever was agreed (or the plugin's own choice, if it agreed to nothing), the audio is mapped to it.
	SpeakerArrangement input = 0, output = 0;
	processor->getBusArrangement (kInput, 0, input);
	processor->getBusArrangement (kOutput, 0, output);
	pluginInputs = SpeakerArr::getChannelCount (input);
	pluginOutputs = SpeakerArr::getChannelCount (output);
	if (pluginInputs < 1 || pluginOutputs < 1)
		fail (pluginName + " doesn't work with mono or stereo audio.");

	for (int32 i = 0; i < inputBuses; ++i)
		component->activateBus (kAudio, kInput, i, i == 0);
	for (int32 i = 0; i < outputBuses; ++i)
		component->activateBus (kAudio, kOutput, i, i == 0);

	rate = sampleRate;
	hostChannels = channels;
	start (false);
}

void Plugin::unload ()
{
	if (!component)
		return;

	stop ();
	data.unprepare ();
	if (componentConnection && controllerConnection)
	{
		componentConnection->disconnect (controllerConnection);
		controllerConnection->disconnect (componentConnection);
	}
	componentConnection = nullptr;
	controllerConnection = nullptr;
	if (editController)
	{
		editController->setComponentHandler (nullptr);
		if (separateController)
			editController->terminate ();
	}
	editController = nullptr;
	processor = nullptr;
	component->terminate ();
	component = nullptr;
	module = nullptr;
}

// ---- Processing ----

void Plugin::stop ()
{
	if (processing)
		processor->setProcessing (false);
	if (active)
		component->setActive (false);
	processing = false;
	active = false;
}

void Plugin::start (bool offline)
{
	stop ();
	ProcessSetup setup {offline ? kOffline : kRealtime, kSample32, maxBlock, rate};
	if (processor->setupProcessing (setup) != kResultTrue)
		fail (pluginName + " can't work at " + std::to_string (static_cast<int> (rate)) + " Hz.");
	if (component->setActive (true) != kResultTrue)
		fail (pluginName + " couldn't be switched on.");
	active = true;
	// Some plugins don't implement this and process anyway, so its result is ignored.
	processor->setProcessing (true);
	processing = true;
	handler.latencyChanged = false;

	if (!data.prepare (*component, maxBlock, kSample32))
		fail ("There isn't enough memory to run " + pluginName + ".");
	data.processMode = setup.processMode;
	data.inputParameterChanges = &inputChanges;
	data.outputParameterChanges = &outputChanges;
	data.processContext = &context;

	// A fixed tempo and bar, for plugins with tempo-synced delays and the like.
	context = {};
	context.state = ProcessContext::kPlaying | ProcessContext::kTempoValid | ProcessContext::kTimeSigValid |
	                ProcessContext::kProjectTimeMusicValid;
	context.sampleRate = rate;
	context.tempo = 120;
	context.timeSigNumerator = 4;
	context.timeSigDenominator = 4;
}

void Plugin::process (float** audio, int32_t frames)
{
	if (!active)
		fail (pluginName + " isn't running.");
	for (int32_t done = 0; done < frames; done += maxBlock)
		processBlock (audio, std::min<int32_t> (maxBlock, frames - done), done);
}

void Plugin::processBlock (float** audio, int32_t frames, int32_t offset)
{
	// The main input gets the song: mono goes to every input, stereo to a mono input is mixed down.
	auto in = data.inputs[0].channelBuffers32;
	for (int c = 0; c < pluginInputs; ++c)
	{
		if (pluginInputs == 1 && hostChannels == 2)
		{
			for (int32_t i = 0; i < frames; ++i)
				in[c][i] = 0.5f * (audio[0][offset + i] + audio[1][offset + i]);
		}
		else
		{
			std::memcpy (in[c], audio[std::min (c, hostChannels - 1)] + offset, sizeof (float) * static_cast<size_t> (frames));
		}
	}
	// Side-chain inputs hear silence.
	for (int32 bus = 1; bus < data.numInputs; ++bus)
	{
		for (int32 c = 0; c < data.inputs[bus].numChannels; ++c)
			std::memset (data.inputs[bus].channelBuffers32[c], 0, sizeof (float) * static_cast<size_t> (frames));
	}
	for (int32 bus = 0; bus < data.numInputs; ++bus)
		data.inputs[bus].silenceFlags = 0;

	data.numSamples = frames;
	inputChanges.clearQueue ();
	outputChanges.clearQueue ();
	handler.takeChanges (inputChanges);
	context.projectTimeMusic = static_cast<double> (context.projectTimeSamples) / rate * context.tempo / 60.0;

	processor->process (data);

	// The main output goes back: a stereo plugin on a mono song is mixed down.
	auto out = data.outputs[0].channelBuffers32;
	for (int c = 0; c < hostChannels; ++c)
	{
		auto target = audio[c] + offset;
		if (hostChannels == 1 && pluginOutputs > 1)
		{
			for (int32_t i = 0; i < frames; ++i)
			{
				float sum = 0;
				for (int p = 0; p < pluginOutputs; ++p)
					sum += out[p][i];
				target[i] = sum / static_cast<float> (pluginOutputs);
			}
		}
		else
		{
			std::memcpy (target, out[std::min (c, pluginOutputs - 1)], sizeof (float) * static_cast<size_t> (frames));
		}
	}
	context.projectTimeSamples += frames;
}

int32_t Plugin::latency () const
{
	return processor ? static_cast<int32_t> (processor->getLatencySamples ()) : 0;
}

int64_t Plugin::tail () const
{
	if (!processor)
		return 0;
	auto samples = processor->getTailSamples ();
	return samples == kInfiniteTail ? -1 : static_cast<int64_t> (samples);
}

// ---- Settings ----

std::vector<char> Plugin::componentState () const
{
	ByteStream stream;
	if (component && component->getState (&stream) == kResultTrue)
		return stream.bytes;
	return {};
}

std::vector<char> Plugin::controllerState () const
{
	ByteStream stream;
	if (editController && editController->getState (&stream) == kResultTrue)
		return stream.bytes;
	return {};
}

void Plugin::setState (const std::vector<char>& componentBytes, const std::vector<char>& controllerBytes)
{
	if (!componentBytes.empty ())
	{
		ByteStream stream (componentBytes);
		component->setState (&stream);
		if (editController)
		{
			ByteStream copy (componentBytes);
			editController->setComponentState (&copy);
		}
	}
	if (!controllerBytes.empty () && editController)
	{
		ByteStream stream (controllerBytes);
		editController->setState (&stream);
	}
	// The settings just loaded already include everything; older knob turns would undo them.
	handler.forgetChanges ();
}

} // namespace Lucent
