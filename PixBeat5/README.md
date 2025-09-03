# 📋 YÊU CẦU PHÁT TRIỂN PHẦN MỀM PIXBEAT 5.0 - SQUARE.BOOM STYLE

## 1. TỔNG QUAN DỰ ÁN

### 1.1 Mục tiêu
Phát triển phần mềm **PixBeat 5.0** - ứng dụng desktop tạo video nhạc pixel art tự động từ file audio, với focus vào style **Square.Boom** (grid ô vuông đập theo nhịp) phổ biến trên TikTok/YouTube Shorts.

### 1.2 Phạm vi
- **Platform**: Windows Desktop (WPF .NET 8)
- **Input**: File nhạc (MP3, WAV, M4A, FLAC)
- **Output**: Video MP4 (H.264 + AAC) với aspect ratio 9:16, 1:1, 16:9
- **Style chính**: Square.Boom Grid (8×8 cells pulsing to music)

### 1.3 Người dùng mục tiêu
- Content creators trên TikTok, Instagram Reels, YouTube Shorts
- Không yêu cầu kỹ năng kỹ thuật
- Cần tạo video nhanh, chất lượng cao, đồng bộ với nhạc

## 2. YÊU CẦU CHỨC NĂNG (FUNCTIONAL REQUIREMENTS)

### 2.1 Audio Analysis Module

#### 2.1.1 Beat Detection
- **Input**: File audio (MP3/WAV/M4A/FLAC)
- **Processing**:
  - Sử dụng librosa (Python) với dynamic programming
  - Sample rate: 44100 Hz
  - Beat tracking với `librosa.beat.beat_track()`
- **Output**: Mảng beat times (seconds) với độ chính xác ±50ms

#### 2.1.2 Onset Detection
- **Mục đích**: Phát hiện điểm bắt đầu của nốt nhạc/âm thanh
- **Processing**:
  - Onset strength envelope calculation
  - Spectral centroid để ước lượng frequency
  - Threshold: strength > 0.7 cho strong onsets
- **Output**: 
  ```json
  {
    "t": 1.234,        // time in seconds
    "strength": 0.85,  // 0.0 to 1.0
    "freq": 3200.0     // frequency in Hz
  }
  ```

#### 2.1.3 Section Detection
- **Mục đích**: Phân đoạn bài hát (intro/verse/chorus/bridge/outro)
- **Processing**:
  - Chroma features analysis
  - Self-similarity matrix
  - Energy level changes detection
  - Minimum section length: 5 seconds
- **Output**: Section boundaries với labels

#### 2.1.4 Loudness Analysis
- **Mục đích**: Track dynamic range cho swell effects
- **Processing**:
  - Short-term LUFS calculation mỗi 0.4s
  - RMS energy envelope
- **Output**: Timeline của loudness values (LUFS)

#### 2.1.5 Key Detection
- **Processing**: Chroma vector analysis
- **Output**: "Root:Mode" (e.g., "C:maj", "Am:min")

### 2.2 Graphics Mapping Module

#### 2.2.1 Event Mapping Rules

| Event Type | Trigger Condition | Visual Action | Duration | Parameters |
|------------|------------------|---------------|----------|------------|
| **Pulse** | On beat | Random 4-8 cells scale up/down | 180ms | scale: 1.0→1.6→1.0 |
| **Flash** | Strong onset (>0.7) | Row flash based on frequency | 120ms | alpha: 0.3→1.0→0.3 |
| **Wipe** | Section change | Grid transition L→R or T→B | 600ms | Progressive reveal |
| **Swell** | Loudness increase >1.5 LU | All cells gentle scale | 400ms | scale: 1.0→1.1→1.0 |

#### 2.2.2 Frequency to Grid Mapping
- **Low frequencies** (100-500 Hz) → Bottom rows (6-7)
- **Mid frequencies** (500-2000 Hz) → Middle rows (3-5)
- **High frequencies** (2000-8000 Hz) → Top rows (0-2)

### 2.3 Rendering Module

#### 2.3.1 Grid Configuration
```json
{
  "rows": 8,        // default, range: 6-12
  "cols": 8,        // default, range: 6-12
  "gap": 8,         // pixels between cells
  "border": 4,      // outer border pixels
  "cornerRadius": 4 // rounded corners
}
```

#### 2.3.2 Visual Effects
- **Cell rendering**:
  - Gradient fill (light→dark diagonal)
  - Rounded corners (4px default, 6px when animating)
  - Inner highlight for 3D effect
  - Glow effect when scale > 1.1

- **Background**:
  - Animated radial gradient
  - Subtle noise texture overlay
  - Vignette effect

- **Particles**:
  - Spawn on beats (3-8 particles)
  - Float upward with physics
  - Fade out over 1 second

#### 2.3.3 Color Palettes

| Palette | Use Case | Background | Cell Colors |
|---------|----------|------------|-------------|
| **Vibrant** | Default/Happy | #000000→#0a0e27 | White, Cyan, Yellow, Red, Purple |
| **Neon** | Electronic/Energetic | #0A0A0A→#1a0033 | Hot pink, Electric blue, Lime |
| **Sunset** | Calm/Chill | #1a0033→#330033 | Warm oranges, Purples, Teals |

### 2.4 Export Module

#### 2.4.1 Video Specifications
- **Codec**: H.264 (libx264)
- **Audio**: AAC
- **Frame rate**: 60 fps (smooth animations)
- **Bitrate**: Variable (CRF 18-28)
- **Pixel format**: yuv420p
- **Resolutions**:
  - 9:16 → 1080×1920 (TikTok/Shorts)
  - 1:1 → 1080×1080 (Instagram)
  - 16:9 → 1920×1080 (YouTube)

#### 2.4.2 Quality Presets
| Preset | CRF | FPS | Use Case |
|--------|-----|-----|----------|
| Draft | 28 | 30 | Quick preview |
| Standard | 23 | 60 | Social media |
| High | 18 | 60 | Professional |

## 3. YÊU CẦU PHI CHỨC NĂNG (NON-FUNCTIONAL REQUIREMENTS)

### 3.1 Performance
- **Render speed**: ≥2× realtime on Intel i5/Ryzen 5
- **Memory usage**: <2GB for 60-second video
- **Multi-threading**: Parallel frame generation
- **Frame accuracy**: Audio-visual sync within ±1 frame

### 3.2 Accuracy
- **Beat detection**: >95% accuracy for electronic/pop music
- **Section boundaries**: ±2 seconds tolerance
- **Onset precision**: >0.8 for clear transients
- **Frequency mapping**: Logarithmic scale 100-8000 Hz

### 3.3 Usability
- **Workflow**: 4 steps max (Import → Analyze → Customize → Export)
- **Processing feedback**: Real-time progress with ETA
- **Error handling**: Clear messages with recovery suggestions
- **Default settings**: Optimal for most use cases

### 3.4 Compatibility
- **OS**: Windows 10/11 64-bit
- **Dependencies**:
  - .NET 8.0 Runtime
  - Python 3.8+ with librosa
  - FFmpeg 4.4+
- **File formats**:
  - Input: MP3, WAV, M4A, FLAC (up to 320kbps)
  - Output: MP4 (H.264/AAC)

## 4. TECHNICAL ARCHITECTURE

### 4.1 Technology Stack
```yaml
Frontend:
  - WPF (.NET 8)
  - MVVM pattern
  - ModernWpfUI

Backend:
  - C# Services (DI pattern)
  - Python scripts (subprocess)
  - Async/await throughout

Graphics:
  - SkiaSharp (2D rendering)
  - Hardware acceleration optional

Audio:
  - NAudio (C# audio I/O)
  - librosa (Python analysis)

Video:
  - FFMpegCore (encoding)
  - Multi-pass encoding
```

### 4.2 Data Flow
```
Audio File → Python Analysis → timeline.json
     ↓
Timeline → Event Mapper → graphics_events.json
     ↓
Events + Template → Frame Generator → PNG frames
     ↓
Frames + Audio → FFmpeg Encoder → MP4 Output
```

### 4.3 Module Structure
```
PixBeat5/
├── Services/
│   ├── AudioAnalysisService.cs      # Orchestrates Python analysis
│   ├── EnhancedSquareBoomRenderService.cs  # Main renderer
│   └── IRenderer.cs                 # Render interface
├── Models/
│   ├── AudioData.cs                 # Audio metadata
│   ├── ProjectData.cs               # Project settings
│   └── RenderProgress.cs            # Progress tracking
├── ViewModels/
│   └── MainViewModel.cs             # UI logic
├── Python/
│   └── analyze_audio_squareboom.py  # Enhanced analysis
└── Templates/
    └── squareboom.json              # Template config
```

## 5. ALGORITHM SPECIFICATIONS

### 5.1 Beat Tracking Algorithm
```python
# Pseudocode
def detect_beats(audio_signal, sample_rate):
    # 1. Compute onset strength
    onset_env = onset_strength(audio_signal)
    
    # 2. Dynamic programming beat tracking
    tempo, beats = beat_track(
        onset_envelope=onset_env,
        sr=sample_rate,
        tightness=100  # Higher = stricter beat grid
    )
    
    # 3. Convert to timestamps
    beat_times = frames_to_time(beats, sr=sample_rate)
    
    return tempo, beat_times
```

### 5.2 Cell Animation Algorithm
```csharp
// Smooth interpolation for cell scaling
foreach (var cell in grid) {
    if (cell.TargetScale != cell.CurrentScale) {
        // Exponential smoothing
        cell.CurrentScale = Lerp(
            cell.CurrentScale, 
            cell.TargetScale, 
            0.15f  // Smoothing factor
        );
        
        // Snap to target when close
        if (Math.Abs(cell.CurrentScale - cell.TargetScale) < 0.01f) {
            cell.CurrentScale = cell.TargetScale;
        }
    }
}
```

### 5.3 Frequency Mapping Algorithm
```python
def map_frequency_to_row(frequency_hz, num_rows):
    # Logarithmic mapping: 100-8000 Hz to row indices
    # High freq → top rows, Low freq → bottom rows
    
    min_freq = 100
    max_freq = 8000
    
    # Normalize to 0-1 using log scale
    normalized = log10(frequency_hz / min_freq) / log10(max_freq / min_freq)
    
    # Invert and map to rows (0=top, num_rows-1=bottom)
    row = int((1 - normalized) * (num_rows - 1))
    
    return clamp(row, 0, num_rows - 1)
```

## 6. UI/UX REQUIREMENTS

### 6.1 Main Window Layout
```
┌─────────────────────────────────────┐
│ 🎵 PixBeat 5.0                      │
│ Square.Boom Video Generator         │
├─────────────────────────────────────┤
│ Step 1: Select Audio                │
│ [Browse...] [song.mp3 ✓]           │
│ BPM: 128 | Genre: Electronic       │
├─────────────────────────────────────┤
│ Step 2: Choose Style                │
│ (•) Square.Boom  ( ) Pixel Runner  │
├─────────────────────────────────────┤
│ Step 3: Settings                    │
│ Duration: [15s ▼] Quality: [HD ▼]  │
│ Aspect: [9:16 TikTok ▼]            │
├─────────────────────────────────────┤
│ [====progress====] 45% | ETA: 30s  │
│                                     │
│        [🎬 Generate Video!]         │
└─────────────────────────────────────┘
```

### 6.2 Feedback Requirements
- **Analysis phase**: "🎵 Analyzing audio with AI..."
- **Rendering phase**: "🎨 Creating Frame X/Y | ETA: XXs"
- **Encoding phase**: "🎥 Encoding HD video..."
- **Success**: "✅ Video ready! [Open Folder] [Play]"

## 7. TESTING REQUIREMENTS

### 7.1 Unit Tests
- Beat detection accuracy with reference tracks
- Frequency mapping correctness
- Event triggering timing
- Color interpolation

### 7.2 Integration Tests
- Full pipeline: Audio → Analysis → Render → Export
- Different audio formats and sample rates
- Various video durations (5s, 15s, 30s, 60s)
- All aspect ratios

### 7.3 Performance Tests
- Render speed benchmarks
- Memory leak detection
- Multi-threading stress test
- Large file handling (>100MB audio)

### 7.4 Visual Quality Tests
- Frame consistency (no drops/glitches)
- Audio-visual synchronization
- Color accuracy (sRGB compliance)
- Compression artifacts check

## 8. ACCEPTANCE CRITERIA

### 8.1 Core Functionality
- ✅ Beats align with grid pulses (±1 frame tolerance)
- ✅ Frequency mapping creates logical row distribution
- ✅ Section transitions are smooth and visible
- ✅ Export creates valid MP4 playable on all platforms

### 8.2 Performance Metrics
- ✅ 30-second video renders in <15 seconds
- ✅ Memory usage stays under 2GB
- ✅ No frame drops at 60fps playback
- ✅ File size ~50MB/minute at 1080p

### 8.3 User Experience
- ✅ Complete workflow in <5 clicks
- ✅ Clear progress indication
- ✅ Graceful error handling
- ✅ Reproducible results with same input

## 9. DELIVERABLES

### 9.1 Software Components
1. **PixBeat5.exe** - Main executable
2. **Python scripts** - Audio analysis
3. **Templates** - Predefined configurations
4. **Documentation** - User guide + API docs

### 9.2 Required Files Structure
```
PixBeat5/
├── PixBeat5.exe
├── Python/
│   ├── analyze_audio_squareboom.py
│   └── requirements.txt
├── Templates/
│   └── squareboom.json
├── Assets/
│   └── (icons, samples)
└── README.md
```

### 9.3 Documentation
- Installation guide with dependency setup
- User manual with workflow examples
- API documentation for extensions
- Troubleshooting guide

## 10. FUTURE ENHANCEMENTS (PHASE 2)

- **Additional Styles**: Wave circles, Particle systems, 3D grids
- **AI Features**: Auto style selection, Music genre detection
- **Social Integration**: Direct upload to TikTok/Instagram
- **Batch Processing**: Multiple songs queue
- **Custom Templates**: User-created grid patterns
- **Real-time Preview**: Live visualization while editing

---

**Document Version**: 1.0  
**Last Updated**: 2024  
**Status**: APPROVED FOR DEVELOPMENT