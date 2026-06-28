cmake_minimum_required(VERSION 3.20)
project(bubi_rcheevos C)

set(RCHEEVOS_ROOT "${CMAKE_CURRENT_SOURCE_DIR}/../../ThirdParty/rcheevos")
set(RCHEEVOS_SOURCES
  ${RCHEEVOS_ROOT}/src/rc_client.c
  ${RCHEEVOS_ROOT}/src/rc_client_raintegration.c
  ${RCHEEVOS_ROOT}/src/rc_compat.c
  ${RCHEEVOS_ROOT}/src/rc_util.c
  ${RCHEEVOS_ROOT}/src/rc_version.c
  ${RCHEEVOS_ROOT}/src/rcheevos/alloc.c
  ${RCHEEVOS_ROOT}/src/rcheevos/condition.c
  ${RCHEEVOS_ROOT}/src/rcheevos/condset.c
  ${RCHEEVOS_ROOT}/src/rcheevos/consoleinfo.c
  ${RCHEEVOS_ROOT}/src/rcheevos/format.c
  ${RCHEEVOS_ROOT}/src/rcheevos/lboard.c
  ${RCHEEVOS_ROOT}/src/rcheevos/memref.c
  ${RCHEEVOS_ROOT}/src/rcheevos/operand.c
  ${RCHEEVOS_ROOT}/src/rcheevos/rc_validate.c
  ${RCHEEVOS_ROOT}/src/rcheevos/richpresence.c
  ${RCHEEVOS_ROOT}/src/rcheevos/runtime.c
  ${RCHEEVOS_ROOT}/src/rcheevos/runtime_progress.c
  ${RCHEEVOS_ROOT}/src/rcheevos/trigger.c
  ${RCHEEVOS_ROOT}/src/rcheevos/value.c
  ${RCHEEVOS_ROOT}/src/rapi/rc_api_common.c
  ${RCHEEVOS_ROOT}/src/rapi/rc_api_editor.c
  ${RCHEEVOS_ROOT}/src/rapi/rc_api_info.c
  ${RCHEEVOS_ROOT}/src/rapi/rc_api_runtime.c
  ${RCHEEVOS_ROOT}/src/rapi/rc_api_user.c
  ${RCHEEVOS_ROOT}/src/rhash/aes.c
  ${RCHEEVOS_ROOT}/src/rhash/cdreader.c
  ${RCHEEVOS_ROOT}/src/rhash/hash.c
  ${RCHEEVOS_ROOT}/src/rhash/hash_disc.c
  ${RCHEEVOS_ROOT}/src/rhash/hash_encrypted.c
  ${RCHEEVOS_ROOT}/src/rhash/hash_rom.c
  ${RCHEEVOS_ROOT}/src/rhash/hash_zip.c
  ${RCHEEVOS_ROOT}/src/rhash/md5.c
)

add_library(bubi_rcheevos SHARED bubi_rcheevos.c ${RCHEEVOS_SOURCES})
target_compile_features(bubi_rcheevos PRIVATE c_std_11)
target_compile_definitions(bubi_rcheevos PRIVATE RC_DISABLE_LUA RC_CLIENT_SUPPORTS_HASH)
target_include_directories(bubi_rcheevos PRIVATE
  ${RCHEEVOS_ROOT}/include
  ${RCHEEVOS_ROOT}/src
  ${RCHEEVOS_ROOT}/src/rcheevos
  ${RCHEEVOS_ROOT}/src/rapi
  ${RCHEEVOS_ROOT}/src/rhash
)
if(CMAKE_C_COMPILER_ID MATCHES "Clang|GNU")
  target_compile_options(bubi_rcheevos PRIVATE -w)
endif()
if(APPLE)
  target_link_libraries(bubi_rcheevos PRIVATE "-framework Security" "-framework CoreFoundation")
elseif(WIN32)
  target_compile_definitions(bubi_rcheevos PRIVATE BUBI_RA_HAS_LIBSECRET=0)
  target_link_libraries(bubi_rcheevos PRIVATE Advapi32)
elseif(UNIX)
  find_package(PkgConfig QUIET)
  if(PkgConfig_FOUND)
    pkg_check_modules(LIBSECRET QUIET libsecret-1)
  endif()

  if(LIBSECRET_FOUND)
    target_compile_definitions(bubi_rcheevos PRIVATE BUBI_RA_HAS_LIBSECRET=1)
    target_include_directories(bubi_rcheevos PRIVATE ${LIBSECRET_INCLUDE_DIRS})
    target_link_directories(bubi_rcheevos PRIVATE ${LIBSECRET_LIBRARY_DIRS})
    target_link_libraries(bubi_rcheevos PRIVATE ${LIBSECRET_LIBRARIES})
  else()
    target_compile_definitions(bubi_rcheevos PRIVATE BUBI_RA_HAS_LIBSECRET=0)
    message(WARNING "libsecret-1 was not found; Linux credential persistence will be unavailable.")
  endif()
else()
  target_compile_definitions(bubi_rcheevos PRIVATE BUBI_RA_HAS_LIBSECRET=0)
endif()
set_target_properties(bubi_rcheevos PROPERTIES
  RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}"
  LIBRARY_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}"
  ARCHIVE_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}"
)

include(CTest)
if(BUILD_TESTING)
  add_executable(bubi_rcheevos_smoke bubi_rcheevos_smoke.c)
  target_link_libraries(bubi_rcheevos_smoke PRIVATE bubi_rcheevos)
  add_test(NAME bubi_rcheevos_smoke COMMAND bubi_rcheevos_smoke)
endif()
