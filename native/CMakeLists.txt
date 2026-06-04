cmake_minimum_required(VERSION 3.20)
project(bubi_miniaudio C)

add_library(bubi_miniaudio SHARED bubi_miniaudio.c)
target_compile_features(bubi_miniaudio PRIVATE c_std_11)

set_target_properties(bubi_miniaudio PROPERTIES
  RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}"
  LIBRARY_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}"
  ARCHIVE_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}"
)

foreach(config DEBUG RELEASE RELWITHDEBINFO MINSIZEREL)
  set_target_properties(bubi_miniaudio PROPERTIES
    RUNTIME_OUTPUT_DIRECTORY_${config} "${CMAKE_BINARY_DIR}"
    LIBRARY_OUTPUT_DIRECTORY_${config} "${CMAKE_BINARY_DIR}"
    ARCHIVE_OUTPUT_DIRECTORY_${config} "${CMAKE_BINARY_DIR}"
  )
endforeach()

if(APPLE)
  target_link_libraries(bubi_miniaudio PRIVATE "-framework CoreAudio" "-framework AudioToolbox")
elseif(UNIX)
  target_link_libraries(bubi_miniaudio PRIVATE m pthread dl)
endif()
