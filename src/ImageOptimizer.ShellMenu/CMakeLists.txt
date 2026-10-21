# Builds ImageOptimizer.ShellMenu.dll, the File Explorer right-click entry in the MSIX package.
# scripts/pack-msix.ps1 builds it with LABELS_DIR pointing at the generated Labels.g.h.
cmake_minimum_required(VERSION 3.21)
project(ImageOptimizerShellMenu LANGUAGES CXX)

set(LABELS_DIR "" CACHE PATH "Folder holding Labels.g.h (the menu text from Strings*.resx)")
if(NOT EXISTS "${LABELS_DIR}/Labels.g.h")
  message(FATAL_ERROR "Labels.g.h wasn't found in LABELS_DIR ('${LABELS_DIR}'). Build it with scripts/pack-msix.ps1.")
endif()

add_library(ImageOptimizer.ShellMenu SHARED ExplorerCommand.cpp ImageOptimizer.ShellMenu.def)
target_compile_features(ImageOptimizer.ShellMenu PRIVATE cxx_std_20)
target_include_directories(ImageOptimizer.ShellMenu PRIVATE "${LABELS_DIR}")
target_compile_definitions(ImageOptimizer.ShellMenu PRIVATE UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
# C4324 comes from WRL's own module.h (an aligned static it pads), not from this code.
target_compile_options(ImageOptimizer.ShellMenu PRIVATE /W4 /WX /wd4324 /permissive- /utf-8 /EHsc)
target_link_libraries(ImageOptimizer.ShellMenu PRIVATE shlwapi runtimeobject)
# Links the C++ runtime in, so the DLL doesn't depend on an installed Visual C++ Redistributable.
set_property(TARGET ImageOptimizer.ShellMenu PROPERTY MSVC_RUNTIME_LIBRARY "MultiThreaded")
