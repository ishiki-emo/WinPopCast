cmake_minimum_required(VERSION 3.24)
project(AppCaptureSpoutBridge LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>") # VC++ ランタイム不要にする

include(FetchContent)
FetchContent_Declare(spout2
  GIT_REPOSITORY https://github.com/leadedge/Spout2.git
  GIT_TAG        2.007h
  GIT_SHALLOW    TRUE
)
# Spout2 自身の CMake は使わず、SpoutDX に必要なソースだけを直接ビルドする
FetchContent_GetProperties(spout2)
if(NOT spout2_POPULATED)
  FetchContent_Populate(spout2)
endif()

set(SPOUTGL ${spout2_SOURCE_DIR}/SPOUTSDK/SpoutGL)
set(SPOUTDX ${spout2_SOURCE_DIR}/SPOUTSDK/SpoutDirectX/SpoutDX)

add_library(AppCaptureSpoutBridge SHARED
  SpoutBridge.cpp
  ${SPOUTDX}/SpoutDX.cpp
  ${SPOUTGL}/SpoutCopy.cpp
  ${SPOUTGL}/SpoutDirectX.cpp
  ${SPOUTGL}/SpoutFrameCount.cpp
  ${SPOUTGL}/SpoutSenderNames.cpp
  ${SPOUTGL}/SpoutSharedMemory.cpp
  ${SPOUTGL}/SpoutUtils.cpp
)
target_include_directories(AppCaptureSpoutBridge PRIVATE ${SPOUTDX} ${SPOUTGL})
target_compile_options(AppCaptureSpoutBridge PRIVATE /utf-8 /W3)
target_compile_definitions(AppCaptureSpoutBridge PRIVATE UNICODE _UNICODE NOMINMAX WIN32_LEAN_AND_MEAN)
target_link_libraries(AppCaptureSpoutBridge PRIVATE d3d11 dxgi winmm shlwapi version)
