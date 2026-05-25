cmake_minimum_required(VERSION 3.20)
project(bubi_miniaudio C)

add_library(bubi_miniaudio SHARED bubi_miniaudio.c)
target_compile_features(bubi_miniaudio PRIVATE c_std_11)

if(APPLE)
  target_link_libraries(bubi_miniaudio PRIVATE "-framework CoreAudio" "-framework AudioToolbox")
elseif(UNIX)
  target_link_libraries(bubi_miniaudio PRIVATE m pthread dl)
endif()
