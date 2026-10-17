// One VST3 effect, loaded and ready to process audio.

#pragma once

#include "public.sdk/source/vst/hosting/module.h"
#include "public.sdk/source/vst/hosting/processdata.h"
#include "public.sdk/source/vst/hosting/parameterchanges.h"
#include "pluginterfaces/vst/ivstaudioprocessor.h"
#include "pluginterfaces/vst/ivstcomponent.h"
#include "pluginterfaces/vst/ivsteditcontroller.h"
#include "pluginterfaces/vst/ivstmessage.h"
#include "pluginterfaces/vst/ivstprocesscontext.h"

#include <atomic>
#include <cstdint>
#include <mutex>
#include <string>
#include <vector>

namespace Lucent {

/// Tells the host application's name to plugins, and collects the knob turns from the plugin's window.
class ComponentHandler : public Steinberg::Vst::IComponentHandler
{
public:
	Steinberg::tresult PLUGIN_API beginEdit (Steinberg::Vst::ParamID) override { return Steinberg::kResultOk; }
	Steinberg::tresult PLUGIN_API performEdit (Steinberg::Vst::ParamID id, Steinberg::Vst::ParamValue value) override;
	Steinberg::tresult PLUGIN_API endEdit (Steinberg::Vst::ParamID) override { return Steinberg::kResultOk; }
	Steinberg::tresult PLUGIN_API restartComponent (Steinberg::int32 flags) override;

	Steinberg::tresult PLUGIN_API queryInterface (const Steinberg::TUID iid, void** obj) override;
	// Lives as long as the plugin; the plugin's reference counting doesn't own it.
	Steinberg::uint32 PLUGIN_API addRef () override { return 1000; }
	Steinberg::uint32 PLUGIN_API release () override { return 1000; }

	/// Hands the changes made since the last call to the processor's next block.
	void takeChanges (Steinberg::Vst::ParameterChanges& into);
	void forgetChanges ();

	std::atomic<bool> latencyChanged {false};

private:
	struct Change
	{
		Steinberg::Vst::ParamID id;
		Steinberg::Vst::ParamValue value;
	};
	std::mutex lock;
	std::vector<Change> changes;
};

class Plugin
{
public:
	~Plugin ();

	/// Loads the effect with the given class id from a .vst3 file. Throws with a message for the user.
	void load (const std::string& path, const std::string& classId, double sampleRate, int channels);

	/// Starts processing from silence, in real-time (preview) or offline (render) mode.
	void start (bool offline);

	/// Processes `frames` frames of planar audio (channels() channels) in place.
	void process (float** audio, int32_t frames);

	int channels () const { return hostChannels; }
	const std::string& name () const { return pluginName; }
	int32_t latency () const;
	/// The tail length the plugin reports, in frames, or -1 for an endless one.
	int64_t tail () const;

	Steinberg::Vst::IEditController* controller () const { return editController.get (); }

	std::vector<char> componentState () const;
	std::vector<char> controllerState () const;
	void setState (const std::vector<char>& component, const std::vector<char>& controllerState);

	void unload ();

	static constexpr int32_t maxBlock = 4096;

private:
	void stop ();
	void processBlock (float** audio, int32_t frames, int32_t offset);

	VST3::Hosting::Module::Ptr module;
	Steinberg::IPtr<Steinberg::Vst::IComponent> component;
	Steinberg::IPtr<Steinberg::Vst::IEditController> editController;
	Steinberg::IPtr<Steinberg::Vst::IAudioProcessor> processor;
	Steinberg::IPtr<Steinberg::Vst::IConnectionPoint> componentConnection;
	Steinberg::IPtr<Steinberg::Vst::IConnectionPoint> controllerConnection;
	bool separateController {false};

	ComponentHandler handler;
	Steinberg::Vst::HostProcessData data;
	Steinberg::Vst::ParameterChanges inputChanges {64};
	Steinberg::Vst::ParameterChanges outputChanges {64};
	Steinberg::Vst::ProcessContext context {};

	std::string pluginName;
	double rate {44100};
	int hostChannels {2};
	int pluginInputs {2};
	int pluginOutputs {2};
	bool active {false};
	bool processing {false};
};

} // namespace Lucent
