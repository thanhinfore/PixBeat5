#!/usr/bin/env python3
"""
Simplified audio analysis for PixBeat 5.0
Usage: python analyze_audio.py <input_audio> <output_json>
"""

import sys
import json
import numpy as np
from pathlib import Path

try:
    import librosa
    LIBROSA_AVAILABLE = True
except ImportError:
    LIBROSA_AVAILABLE = False
    print('Warning: librosa not installed, using simplified analysis', file=sys.stderr)

def analyze_audio(audio_path: str, output_path: str):
    """Analyze audio file and export simplified results"""
    
    if LIBROSA_AVAILABLE:
        analyze_with_librosa(audio_path, output_path)
    else:
        analyze_simple(audio_path, output_path)

def analyze_with_librosa(audio_path: str, output_path: str):
    """Full analysis using librosa"""
    try:
        # Load audio
        y, sr = librosa.load(audio_path, sr=44100, mono=True)
        duration = len(y) / sr
        
        # Basic tempo and beat tracking
        tempo, beats = librosa.beat.beat_track(y=y, sr=sr)
        
        # Convert tempo to scalar if it's an array
        if isinstance(tempo, np.ndarray):
            tempo = float(tempo[0]) if tempo.size > 0 else 120.0
        else:
            tempo = float(tempo)
        
        # Get beat times (not frames)
        beat_times = librosa.frames_to_time(beats, sr=sr)
        beat_times_list = beat_times.tolist() if isinstance(beat_times, np.ndarray) else list(beat_times)
        # Limit to 1000 beats for performance
        beat_times_list = beat_times_list[:1000]
        
        # Simple energy analysis (RMS)
        rms = librosa.feature.rms(y=y)[0]
        # Downsample for performance
        energy_levels = rms[::10].tolist()[:1000]
        
        # Basic genre classification (simplified)
        spectral_centroids = librosa.feature.spectral_centroid(y=y, sr=sr)[0]
        avg_centroid = float(np.mean(spectral_centroids))
        
        if avg_centroid > 3000:
            genre = "Electronic"
        elif avg_centroid > 2000:
            genre = "Pop"
        elif avg_centroid > 1500:
            genre = "Rock"
        else:
            genre = "Classical"
            
        # Simple key detection
        chroma = librosa.feature.chroma_stft(y=y, sr=sr)
        key_profiles = ['C', 'C#', 'D', 'D#', 'E', 'F', 'F#', 'G', 'G#', 'A', 'A#', 'B']
        key_index = int(np.argmax(np.sum(chroma, axis=1)))
        key = key_profiles[key_index]
        
        # Simple mood detection based on tempo and energy
        avg_energy = float(np.mean(rms))
        if tempo > 120 and avg_energy > 0.1:
            mood = "Energetic"
        elif tempo < 80:
            mood = "Calm"
        elif avg_energy > 0.15:
            mood = "Happy"
        else:
            mood = "Neutral"
        
        # Mode detection
        mode = "major" if avg_energy > 0.1 else "minor"
            
        # Confidence score based on beat tracking strength
        onset_strength = librosa.onset.onset_strength(y=y, sr=sr)
        confidence = float(min(np.mean(onset_strength) / 0.5, 1.0))
        
        # Prepare results - ensure all values are JSON serializable
        results = {
            "tempo": tempo,
            "beat_times": beat_times_list,
            "energy_levels": [float(e) for e in energy_levels],  # Ensure float conversion
            "genre": genre,
            "key": key,
            "mode": mode,
            "mood": mood,
            "confidence": confidence
        }
        
        # Save results
        with open(output_path, 'w') as f:
            json.dump(results, f, indent=2)
            
        print(f"Analysis complete: BPM={tempo:.1f}, Genre={genre}, Key={key}")
        
    except Exception as e:
        print(f"Error during analysis: {e}", file=sys.stderr)
        print(f"Error type: {type(e).__name__}", file=sys.stderr)
        import traceback
        traceback.print_exc(file=sys.stderr)
        sys.exit(1)

def analyze_simple(audio_path: str, output_path: str):
    """Simplified analysis without librosa"""
    
    results = {
        "tempo": 120.0,
        "beat_times": [i * 0.5 for i in range(120)],
        "energy_levels": [0.5 + 0.3 * float(np.sin(i * 0.1)) for i in range(100)],
        "genre": "Unknown",
        "key": "C",
        "mode": "major",
        "mood": "Neutral",
        "confidence": 0.5
    }
    
    with open(output_path, 'w') as f:
        json.dump(results, f, indent=2)
    
    print("Simple analysis complete (librosa not available)")

if __name__ == "__main__":
    if len(sys.argv) != 3:
        print("Usage: python analyze_audio.py <input_audio> <output_json>")
        sys.exit(1)
    
    audio_path = sys.argv[1]
    output_path = sys.argv[2]
    
    if not Path(audio_path).exists():
        print(f"Error: Audio file not found: {audio_path}", file=sys.stderr)
        sys.exit(1)
    
    analyze_audio(audio_path, output_path)