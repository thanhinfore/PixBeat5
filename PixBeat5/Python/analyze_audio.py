#!/usr/bin/env python3
"""
Simplified audio analysis for PixBeat 5.0
Usage: python analyze_audio.py <input_audio> <output_json>
"""

import sys
import json
import librosa
import numpy as np
from pathlib import Path

def analyze_audio(audio_path: str, output_path: str):
    """Analyze audio file and export simplified results"""
    
    try:
        # Load audio
        y, sr = librosa.load(audio_path, sr=44100, mono=True)
        duration = len(y) / sr
        
        # Basic tempo and beat tracking
        tempo, beat_frames = librosa.beat.beat_track(y=y, sr=sr, units='time')
        beat_times = beat_frames.tolist()
        
        # Simple energy analysis (RMS)
        frame_length = 2048
        hop_length = 512
        rms = librosa.feature.rms(y=y, frame_length=frame_length, hop_length=hop_length)[0]
        
        # Basic genre classification (simplified)
        spectral_centroids = librosa.feature.spectral_centroid(y=y, sr=sr)[0]
        avg_centroid = np.mean(spectral_centroids)
        
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
        key_index = np.argmax(np.sum(chroma, axis=1))
        key = key_profiles[key_index]
        
        # Simple mood detection based on tempo and energy
        avg_energy = np.mean(rms)
        if tempo > 120 and avg_energy > 0.1:
            mood = "Energetic"
        elif tempo < 80:
            mood = "Calm"
        elif avg_energy > 0.15:
            mood = "Happy"
        else:
            mood = "Neutral"
            
        # Confidence score based on beat tracking strength
        onset_strength = librosa.onset.onset_strength(y=y, sr=sr)
        confidence = min(np.mean(onset_strength) / 0.5, 1.0)
        
        # Prepare results
        results = {
            "tempo": float(tempo),
            "beat_times": beat_times[:min(len(beat_times), 1000)],  # Limit for performance
            "energy_levels": rms[::10].tolist(),  # Downsample for performance
            "genre": genre,
            "key": key,
            "mode": "major" if avg_energy > 0.1 else "minor",
            "mood": mood,
            "confidence": confidence
        }
        
        # Save results
        with open(output_path, 'w') as f:
            json.dump(results, f, indent=2)
            
        print(f"Analysis complete: BPM={tempo:.1f}, Genre={genre}, Key={key}")
        
    except Exception as e:
        print(f"Error during analysis: {e}")
        sys.exit(1)

if __name__ == "__main__":
    if len(sys.argv) != 3:
        print("Usage: python analyze_audio.py <input_audio> <output_json>")
        sys.exit(1)
    
    audio_path = sys.argv[1]
    output_path = sys.argv[2]
    
    if not Path(audio_path).exists():
        print(f"Error: Audio file not found: {audio_path}")
        sys.exit(1)
    
    analyze_audio(audio_path, output_path)