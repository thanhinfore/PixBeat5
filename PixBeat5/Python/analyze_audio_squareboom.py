#!/usr/bin/env python3
"""
Enhanced audio analysis for Square.Boom style videos
Optimized for beat-reactive grid animations
"""

import sys
import json
import numpy as np
from pathlib import Path

try:
    import librosa
    import librosa.display
    LIBROSA_AVAILABLE = True
except ImportError:
    LIBROSA_AVAILABLE = False
    print('Warning: librosa not installed, using simplified analysis', file=sys.stderr)

def analyze_audio_squareboom(audio_path: str, output_path: str):
    """Enhanced analysis specifically for Square.Boom grid videos"""
    
    if not LIBROSA_AVAILABLE:
        print("Error: librosa is required for Square.Boom analysis", file=sys.stderr)
        sys.exit(1)
    
    try:
        # Load audio with higher sample rate for better onset detection
        y, sr = librosa.load(audio_path, sr=44100, mono=True)
        duration = len(y) / sr
        
        # 1. ENHANCED BEAT TRACKING with dynamic programming
        print("Detecting beats...", file=sys.stderr)
        tempo, beats = librosa.beat.beat_track(
            y=y, sr=sr, 
            units='time',
            trim=False
        )
        
        # Convert tempo to scalar
        if isinstance(tempo, np.ndarray):
            tempo = float(tempo[0]) if tempo.size > 0 else 120.0
        else:
            tempo = float(tempo)
        
        # Get beat times (not frames)
        beat_times = beats.tolist() if hasattr(beats, 'tolist') else list(beats)
        beat_times = beat_times[:1000]  # Limit for performance
        
        # 2. ONSET DETECTION with strength and frequency
        print("Detecting onsets...", file=sys.stderr)
        onset_env = librosa.onset.onset_strength(y=y, sr=sr)
        onset_frames = librosa.onset.onset_detect(
            onset_envelope=onset_env, 
            sr=sr,
            units='time',
            backtrack=True
        )
        
        # Calculate spectral centroids for frequency estimation
        spectral_centroids = librosa.feature.spectral_centroid(y=y, sr=sr)[0]
        
        # Create onset events with strength and frequency
        onsets = []
        for i, onset_time in enumerate(onset_frames[:500]):  # Limit onsets
            frame_idx = librosa.time_to_frames(onset_time, sr=sr)
            if frame_idx < len(onset_env) and frame_idx < len(spectral_centroids):
                strength = float(onset_env[frame_idx] / np.max(onset_env))
                freq = float(spectral_centroids[frame_idx])
                
                onsets.append({
                    "t": float(onset_time),
                    "strength": strength,
                    "freq": freq
                })
        
        # 3. SECTION DETECTION using structural features
        print("Detecting sections...", file=sys.stderr)
        
        # Compute chroma features for harmonic analysis
        chroma = librosa.feature.chroma_stft(y=y, sr=sr)
        
        # Compute self-similarity matrix
        chroma_sim = librosa.segment.recurrence_matrix(
            chroma, 
            mode='affinity',
            metric='cosine'
        )
        
        # Detect section boundaries using novelty
        novelty = librosa.segment.lag_to_recurrence_matrix(
            librosa.segment.novelty_curve(chroma_sim)
        )
        
        # Simple section detection based on energy and spectral changes
        hop_length = 512
        frame_length = 2048
        
        # RMS energy
        rms = librosa.feature.rms(y=y, frame_length=frame_length, hop_length=hop_length)[0]
        
        # Spectral rolloff for brightness
        rolloff = librosa.feature.spectral_rolloff(y=y, sr=sr, hop_length=hop_length)[0]
        
        # Detect sections based on energy and spectral changes
        sections = [{"t": 0.0, "label": "intro"}]
        
        # Moving average for smoothing
        window_size = 43  # ~1 second at 44100/1024
        rms_smooth = np.convolve(rms, np.ones(window_size)/window_size, mode='same')
        
        # Find significant changes
        rms_diff = np.diff(rms_smooth)
        threshold = np.std(rms_diff) * 1.5
        
        section_times = []
        min_section_length = sr * 5 / hop_length  # Minimum 5 seconds between sections
        
        for i in range(1, len(rms_diff)):
            if abs(rms_diff[i]) > threshold:
                time = librosa.frames_to_time(i, sr=sr, hop_length=hop_length)
                if not section_times or (i - section_times[-1][0]) > min_section_length:
                    section_times.append((i, time, rms_diff[i]))
        
        # Label sections based on energy levels
        avg_energy = np.mean(rms)
        current_label = "intro"
        
        for frame_idx, time, diff in section_times:
            if time < 10:  # Skip very early changes
                continue
                
            local_energy = np.mean(rms[max(0, frame_idx-window_size):min(len(rms), frame_idx+window_size)])
            
            if local_energy > avg_energy * 1.3:
                label = "chorus"
            elif local_energy < avg_energy * 0.7:
                label = "bridge" if current_label == "verse" else "verse"
            else:
                label = "verse" if current_label != "verse" else "bridge"
            
            sections.append({"t": float(time), "label": label})
            current_label = label
        
        # Add outro
        if duration > 20:
            sections.append({"t": float(duration - 5), "label": "outro"})
        
        # 4. LOUDNESS TIMELINE (short-term energy)
        print("Calculating loudness...", file=sys.stderr)
        
        # Calculate short-term loudness every 0.4 seconds
        loudness = []
        window_samples = int(sr * 0.4)
        
        for i in range(0, len(y) - window_samples, window_samples // 2):
            window = y[i:i+window_samples]
            # Simple LUFS approximation
            rms_val = np.sqrt(np.mean(window**2))
            lufs = -0.691 + 10 * np.log10(rms_val + 1e-10)
            loudness.append({
                "t": float(i / sr),
                "lufs": float(lufs)
            })
        
        # 5. KEY DETECTION (enhanced)
        print("Detecting key...", file=sys.stderr)
        
        # Chroma with higher resolution
        chromagram = librosa.feature.chroma_cqt(y=y, sr=sr)
        
        # Krumhansl-Schmuckler key-finding algorithm
        key_profiles = {
            'C': 0, 'C#': 1, 'D': 2, 'D#': 3, 'E': 4, 'F': 5,
            'F#': 6, 'G': 7, 'G#': 8, 'A': 9, 'A#': 10, 'B': 11
        }
        
        # Average chroma vector
        chroma_avg = np.mean(chromagram, axis=1)
        key_idx = np.argmax(chroma_avg)
        key = list(key_profiles.keys())[key_idx]
        
        # Determine mode (major/minor) based on third interval
        third_idx = (key_idx + 4) % 12  # Major third
        minor_third_idx = (key_idx + 3) % 12  # Minor third
        
        if chroma_avg[third_idx] > chroma_avg[minor_third_idx]:
            mode = "maj"
        else:
            mode = "min"
        
        # 6. Additional metadata
        # Energy distribution
        energy_percentiles = np.percentile(rms, [25, 50, 75])
        
        # Spectral features for genre hints
        spectral_contrast = librosa.feature.spectral_contrast(y=y, sr=sr)
        avg_contrast = np.mean(spectral_contrast, axis=1)
        
        # Zero crossing rate for percussion detection
        zcr = librosa.feature.zero_crossing_rate(y)[0]
        avg_zcr = np.mean(zcr)
        
        # Compile results
        results = {
            "sr": sr,
            "duration": float(duration),
            "bpm": tempo,
            "beats": beat_times,
            "onsets": onsets,
            "sections": sections,
            "loudness": loudness,
            "key": f"{key}:{mode}",
            "meta": {
                "source": str(audio_path),
                "energy_distribution": {
                    "q25": float(energy_percentiles[0]),
                    "median": float(energy_percentiles[1]),
                    "q75": float(energy_percentiles[2])
                },
                "spectral_contrast": avg_contrast.tolist(),
                "avg_zcr": float(avg_zcr),
                "confidence": float(np.mean(onset_env) / (np.std(onset_env) + 1e-10))
            }
        }
        
        # Save timeline.json
        with open(output_path, 'w') as f:
            json.dump(results, f, indent=2)
        
        print(f"✓ Square.Boom analysis complete: BPM={tempo:.1f}, Sections={len(sections)}, Key={key}:{mode}")
        
    except Exception as e:
        print(f"Error during Square.Boom analysis: {e}", file=sys.stderr)
        import traceback
        traceback.print_exc(file=sys.stderr)
        sys.exit(1)

if __name__ == "__main__":
    if len(sys.argv) != 3:
        print("Usage: python analyze_audio_squareboom.py <input_audio> <output_json>")
        sys.exit(1)
    
    audio_path = sys.argv[1]
    output_path = sys.argv[2]
    
    if not Path(audio_path).exists():
        print(f"Error: Audio file not found: {audio_path}", file=sys.stderr)
        sys.exit(1)
    
    analyze_audio_squareboom(audio_path, output_path)