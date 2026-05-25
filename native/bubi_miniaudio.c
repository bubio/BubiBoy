#define MINIAUDIO_IMPLEMENTATION
#include "miniaudio.h"

#include <stdint.h>
#include <stdlib.h>
#include <string.h>

typedef struct bubi_audio_device {
    ma_device device;
    ma_mutex mutex;
    int16_t* buffer;
    ma_uint32 capacity_frames;
    ma_uint32 read_index;
    ma_uint32 count;
    ma_uint32 channels;
    ma_uint64 underrun_frames;
    ma_uint64 dropped_frames;
} bubi_audio_device;

static void bubi_audio_callback(ma_device* device, void* output, const void* input, ma_uint32 frame_count)
{
    (void)input;

    bubi_audio_device* audio = (bubi_audio_device*)device->pUserData;
    int16_t* out = (int16_t*)output;
    ma_uint32 channels = audio->channels;
    ma_uint32 frames_read = 0;

    ma_mutex_lock(&audio->mutex);

    while (frames_read < frame_count && audio->count > 0) {
        ma_uint32 source = audio->read_index * channels;
        ma_uint32 target = frames_read * channels;

        for (ma_uint32 channel = 0; channel < channels; channel++) {
            out[target + channel] = audio->buffer[source + channel];
        }

        audio->read_index = (audio->read_index + 1) % audio->capacity_frames;
        audio->count -= 1;
        frames_read += 1;
    }

    ma_mutex_unlock(&audio->mutex);

    if (frames_read < frame_count) {
        audio->underrun_frames += (ma_uint64)(frame_count - frames_read);
        memset(out + frames_read * channels, 0, (frame_count - frames_read) * channels * sizeof(int16_t));
    }
}

#if defined(_WIN32)
#define BUBI_EXPORT __declspec(dllexport)
#else
#define BUBI_EXPORT __attribute__((visibility("default")))
#endif

BUBI_EXPORT bubi_audio_device* bubi_audio_create(int sample_rate, int channels, int buffer_frames)
{
    if (sample_rate <= 0 || channels != 2 || buffer_frames <= 0) {
        return NULL;
    }

    bubi_audio_device* audio = (bubi_audio_device*)calloc(1, sizeof(bubi_audio_device));
    if (audio == NULL) {
        return NULL;
    }

    audio->buffer = (int16_t*)calloc((size_t)buffer_frames * (size_t)channels, sizeof(int16_t));
    if (audio->buffer == NULL) {
        free(audio);
        return NULL;
    }

    audio->capacity_frames = (ma_uint32)buffer_frames;
    audio->channels = (ma_uint32)channels;

    if (ma_mutex_init(&audio->mutex) != MA_SUCCESS) {
        free(audio->buffer);
        free(audio);
        return NULL;
    }

    ma_device_config config = ma_device_config_init(ma_device_type_playback);
    config.playback.format = ma_format_s16;
    config.playback.channels = (ma_uint32)channels;
    config.sampleRate = (ma_uint32)sample_rate;
    config.dataCallback = bubi_audio_callback;
    config.pUserData = audio;

    if (ma_device_init(NULL, &config, &audio->device) != MA_SUCCESS) {
        ma_mutex_uninit(&audio->mutex);
        free(audio->buffer);
        free(audio);
        return NULL;
    }

    return audio;
}

BUBI_EXPORT void bubi_audio_destroy(bubi_audio_device* audio)
{
    if (audio == NULL) {
        return;
    }

    ma_device_stop(&audio->device);
    ma_device_uninit(&audio->device);
    ma_mutex_uninit(&audio->mutex);
    free(audio->buffer);
    free(audio);
}

BUBI_EXPORT int bubi_audio_start(bubi_audio_device* audio)
{
    if (audio == NULL) {
        return -1;
    }

    return ma_device_start(&audio->device) == MA_SUCCESS ? 0 : -1;
}

BUBI_EXPORT int bubi_audio_stop(bubi_audio_device* audio)
{
    if (audio == NULL) {
        return -1;
    }

    return ma_device_stop(&audio->device) == MA_SUCCESS ? 0 : -1;
}

BUBI_EXPORT int bubi_audio_enqueue_pcm16(bubi_audio_device* audio, const uint8_t* pcm_bytes, int frames)
{
    if (audio == NULL || pcm_bytes == NULL || frames <= 0) {
        return 0;
    }

    int accepted = 0;
    const int16_t* samples = (const int16_t*)pcm_bytes;

    ma_mutex_lock(&audio->mutex);

    for (int frame = 0; frame < frames; frame++) {
        if (audio->count == audio->capacity_frames) {
            audio->read_index = (audio->read_index + 1) % audio->capacity_frames;
            audio->count -= 1;
            audio->dropped_frames += 1;
        }

        ma_uint32 write_index = (audio->read_index + audio->count) % audio->capacity_frames;
        ma_uint32 target = write_index * audio->channels;
        ma_uint32 source = (ma_uint32)frame * audio->channels;

        for (ma_uint32 channel = 0; channel < audio->channels; channel++) {
            audio->buffer[target + channel] = samples[source + channel];
        }

        audio->count += 1;
        accepted += 1;
    }

    ma_mutex_unlock(&audio->mutex);

    return accepted;
}

BUBI_EXPORT int bubi_audio_buffered_frames(bubi_audio_device* audio)
{
    if (audio == NULL) {
        return 0;
    }

    ma_mutex_lock(&audio->mutex);
    int count = (int)audio->count;
    ma_mutex_unlock(&audio->mutex);
    return count;
}

BUBI_EXPORT uint64_t bubi_audio_underrun_frames(bubi_audio_device* audio)
{
    if (audio == NULL) {
        return 0;
    }

    ma_mutex_lock(&audio->mutex);
    uint64_t frames = (uint64_t)audio->underrun_frames;
    ma_mutex_unlock(&audio->mutex);
    return frames;
}

BUBI_EXPORT uint64_t bubi_audio_dropped_frames(bubi_audio_device* audio)
{
    if (audio == NULL) {
        return 0;
    }

    ma_mutex_lock(&audio->mutex);
    uint64_t frames = (uint64_t)audio->dropped_frames;
    ma_mutex_unlock(&audio->mutex);
    return frames;
}
