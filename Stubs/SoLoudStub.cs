// Navigation-only stubs for SoLoud (SoLoudLib.dll).
// SoLoudLib was compiled for .NET 9 and cannot be referenced against the .NET 8 SDK.
// These stubs expose the minimal API surface used by Allumeria's Audio classes
// so the source compiles and IntelliSense works.

namespace SoLoud
{
  public abstract class SoloudObject { }

  public class Speech : SoloudObject
  {
    public void setText(string text) { }
    public void setVolume(float volume) { }
    public void setLooping(bool looping) { }
  }

  public class Wav : SoloudObject
  {
    public void load(string path) { }
    public void loadMem(byte[] data, uint dataLen, bool copy = false, bool takeOwnership = true) { }
    public double getLength() => 0;
    public void setVolume(float volume) { }
    public void setLooping(bool looping) { }
    public void setLooping(int looping) { }
    public void setInaudibleBehavior(int mustTick, int kill) { }
    public void setLoopPoint(double loopPoint) { }
  }

  public class WavStream : SoloudObject
  {
    public void load(string path) { }
    public double getLength() => 0;
    public void setVolume(float volume) { }
    public void setLooping(bool looping) { }
  }

  public class Bus : SoloudObject
  {
    public uint play(SoloudObject sound, float volume = -1f, float pan = 0f, bool paused = false) => 0;
    public uint play3d(SoloudObject sound, float x, float y, float z,
      float velX = 0f, float velY = 0f, float velZ = 0f,
      float aVolume = -1f, bool paused = false) => 0;
    public void setVolume(float volume) { }
    public float getVolume() => 1f;
  }

  public class Soloud
  {
    public const int CLIP_SOFT = 1;

    public uint init(uint flags = 0, uint backend = 0, uint samplerate = 0, uint bufferSize = 0, uint channels = 2) => 0;
    public void deinit() { }

    public uint play(SoloudObject sound, float volume = -1f, float pan = 0f, bool paused = false, uint bus = 0) => 0;
    public uint playBackground(SoloudObject sound, float volume = -1f, bool paused = false, uint bus = 0) => 0;
    public uint playClocked(double soundTime, SoloudObject sound, float volume = -1f, float pan = 0f, uint bus = 0) => 0;

    public void stop(uint voiceHandle) { }
    public void stopAll() { }
    public void stopAudioSource(SoloudObject sound) { }

    public void setVolume(uint voiceHandle, float volume) { }
    public float getVolume(uint voiceHandle) => 1f;
    public void fadeVolume(uint voiceHandle, float to, double time) { }
    public void setGlobalVolume(float volume) { }
    public float getGlobalVolume() => 1f;
    public void fadeGlobalVolume(float to, double time) { }

    public void setPause(uint voiceHandle, bool pause) { }
    public bool getPause(uint voiceHandle) => false;
    public void setPauseAll(bool pause) { }

    public void setLooping(uint voiceHandle, bool looping) { }
    public void setRelativePlaySpeed(uint voiceHandle, float speed) { }
    public void setMaxActiveVoiceCount(uint voiceCount) { }

    public void scheduleStop(uint voiceHandle, double time) { }
    public void schedulePause(uint voiceHandle, double time) { }

    public bool isValidVoiceHandle(uint voiceHandle) => false;
    public uint getActiveVoiceCount() => 0;
    public double getStreamTime(uint voiceHandle) => 0;

    public void setProtectVoice(uint voiceHandle, bool protect) { }

    public void set3dListenerPosition(float x, float y, float z) { }
    public void set3dListenerAt(float atX, float atY, float atZ) { }
    public void set3dListenerUp(float upX, float upY, float upZ) { }
    public void update3dAudio() { }
  }
}
