#!/usr/bin/env python3
"""Test script to debug librosa issues"""

import sys
import numpy as np

print("Python version:", sys.version)
print("NumPy version:", np.__version__)

try:
    import librosa
    print("Librosa version:", librosa.__version__)
    
    # Test basic librosa functions
    print("\nTesting librosa functions...")
    
    # Create test signal
    sr = 22050
    duration = 5
    y = np.random.randn(sr * duration)
    
    # Test tempo detection
    print("Testing tempo detection...")
    tempo, beats = librosa.beat.beat_track(y=y, sr=sr)
    print(f"  Tempo type: {type(tempo)}")
    print(f"  Tempo value: {tempo}")
    
    if isinstance(tempo, np.ndarray):
        print(f"  Tempo shape: {tempo.shape}")
        tempo = float(tempo[0]) if tempo.size > 0 else 120.0
    
    print(f"  Tempo (converted): {tempo:.1f} BPM")
    
    # Test beat frames to time
    print("\nTesting beat frames to time...")
    beat_times = librosa.frames_to_time(beats, sr=sr)
    print(f"  Beat times type: {type(beat_times)}")
    print(f"  Number of beats: {len(beat_times)}")
    print(f"  First 5 beat times: {beat_times[:5].tolist()}")
    
    # Test RMS
    print("\nTesting RMS energy...")
    rms = librosa.feature.rms(y=y)[0]
    print(f"  RMS shape: {rms.shape}")
    print(f"  RMS mean: {np.mean(rms):.4f}")
    
    print("\n✅ All tests passed!")
    
except ImportError as e:
    print(f"❌ Librosa not installed: {e}")
except Exception as e:
    print(f"❌ Error during testing: {e}")
    import traceback
    traceback.print_exc()