# LucentPluginHost: the helper program that runs VST3 plugins for Lucent's audio editor.
#
# Lucent starts one of these for each plugin it opens and talks to it over its standard input and
# output, so a plugin that crashes takes only this helper down, never Lucent. See source/protocol.h.
#
# Build (Windows, from a Visual Studio developer prompt or with CMake on the PATH):
#   cmake -S src/PluginHost -B build/PluginHost -A x64
#   cmake --build build/PluginHost --config Release
# The Steinberg VST3 SDK (MIT licence) is downloaded at configure time. To use a copy you already
# have, pass -DLUCENT_VST3SDK_DIR=path/to/vst3sdk.

cmake_minimum_required(VERSION 3.20)
project(LucentPluginHost LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

set(LUCENT_VST3SDK_DIR "" CACHE PATH "An existing VST3 SDK checkout (leave empty to download it)")
if(LUCENT_VST3SDK_DIR)
    set(SDK "${LUCENT_VST3SDK_DIR}")
else()
    include(FetchContent)
    # Only the parts needed to host plugins; VSTGUI, the examples and the docs are left out.
    FetchContent_Declare(vst3sdk
        GIT_REPOSITORY https://github.com/steinbergmedia/vst3sdk.git
        GIT_TAG v3.8.0_build_66
        GIT_SHALLOW TRUE
        GIT_SUBMODULES base pluginterfaces public.sdk
        # The SDK's own CMake project builds far more than this needs; a folder with no
        # CMakeLists.txt makes FetchContent download it without adding it to the build.
        SOURCE_SUBDIR do-not-build
    )
    FetchContent_MakeAvailable(vst3sdk)
    set(SDK "${vst3sdk_SOURCE_DIR}")
endif()

set(HOSTING "${SDK}/public.sdk/source/vst/hosting")
set(SDK_SOURCES
    ${SDK}/base/source/baseiids.cpp
    ${SDK}/base/source/fobject.cpp
    ${SDK}/base/source/fdebug.cpp
    ${SDK}/base/source/fstring.cpp
    ${SDK}/base/source/updatehandler.cpp
    ${SDK}/base/thread/source/flock.cpp
    ${SDK}/pluginterfaces/base/conststringtable.cpp
    ${SDK}/pluginterfaces/base/coreiids.cpp
    ${SDK}/pluginterfaces/base/funknown.cpp
    ${SDK}/pluginterfaces/base/ustring.cpp
    ${SDK}/public.sdk/source/common/commoniids.cpp
    ${SDK}/public.sdk/source/common/commonstringconvert.cpp
    ${SDK}/public.sdk/source/vst/utility/stringconvert.cpp
    ${SDK}/public.sdk/source/vst/vstinitiids.cpp
    ${HOSTING}/hostclasses.cpp
    ${HOSTING}/module.cpp
    ${HOSTING}/parameterchanges.cpp
    ${HOSTING}/pluginterfacesupport.cpp
    ${HOSTING}/processdata.cpp
)
if(WIN32)
    list(APPEND SDK_SOURCES ${HOSTING}/module_win32.cpp)
elseif(APPLE)
    list(APPEND SDK_SOURCES ${HOSTING}/module_mac.mm)
else()
    list(APPEND SDK_SOURCES ${HOSTING}/module_linux.cpp)
endif()

set(HOST_SOURCES
    source/main.cpp
    source/channel.cpp
    source/plugin.cpp
)
if(WIN32)
    list(APPEND HOST_SOURCES source/editor_win32.cpp)
else()
    # No plugin windows on other systems yet; processing works everywhere.
    list(APPEND HOST_SOURCES source/editor_none.cpp)
endif()

# WIN32: no console window flashes up when Lucent starts it.
add_executable(LucentPluginHost WIN32 ${HOST_SOURCES} ${SDK_SOURCES})
target_include_directories(LucentPluginHost PRIVATE "${SDK}" source)
target_compile_definitions(LucentPluginHost PRIVATE RELEASE=1)

if(MSVC)
    # One self-contained exe: no Visual C++ runtime to install.
    set_property(TARGET LucentPluginHost PROPERTY MSVC_RUNTIME_LIBRARY "MultiThreaded")
    target_compile_options(LucentPluginHost PRIVATE /utf-8 /W3)
    target_compile_definitions(LucentPluginHost PRIVATE UNICODE _UNICODE NOMINMAX WIN32_LEAN_AND_MEAN)
elseif(WIN32)
    target_compile_definitions(LucentPluginHost PRIVATE UNICODE _UNICODE NOMINMAX WIN32_LEAN_AND_MEAN)
    target_link_options(LucentPluginHost PRIVATE -municode -static)
endif()

if(WIN32)
    target_link_libraries(LucentPluginHost PRIVATE ole32 user32)
elseif(APPLE)
    target_link_libraries(LucentPluginHost PRIVATE "-framework CoreFoundation" "-framework Foundation")
else()
    target_link_libraries(LucentPluginHost PRIVATE dl pthread)
endif()
