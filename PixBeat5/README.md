# PixBeat 5.0 🎵

**Simple Pixel Art Music Video Generator**

PixBeat 5.0 là ứng dụng desktop đơn giản để tạo video pixel art đồng bộ với nhạc. Chỉ với 4 bước đơn giản: Import Audio → AI Analysis → Choose Template → Generate Video!

![PixBeat Logo](docs/images/pixbeat-banner.png)

## ✨ Features

- 🤖 **AI-Powered Audio Analysis** - Tự động phát hiện BPM, genre, key, mood
- 🎨 **Beautiful Templates** - Pixel Runner, Audio Equalizer, Waveform Visualizer
- 📱 **Social Media Ready** - Export 9:16 (TikTok/Shorts), 1:1 (Instagram), 16:9 (YouTube)
- ⚡ **Fast Rendering** - Multi-threaded rendering với progress tracking
- 🎯 **Simple Workflow** - Không cần technical knowledge
- 💾 **Portable** - Single executable, không cần installation phức tạp

## 🎬 Demo

![Demo GIF](docs/images/demo.gif)

### Sample Outputs

| Template | Preview | Best For |
|----------|---------|----------|
| **Pixel Runner** | ![Runner Preview](docs/images/pixel-runner-preview.png) | Gaming, Retro, Upbeat music |
| **Audio Equalizer** | ![Equalizer Preview](docs/images/equalizer-preview.png) | Electronic, Dance, Bass-heavy |
| **Waveform Visualizer** | ![Waveform Preview](docs/images/waveform-preview.png) | Ambient, Chill, Acoustic |

## 📋 System Requirements

### Minimum Requirements
- **OS**: Windows 10 (64-bit) hoặc newer
- **RAM**: 4GB (8GB recommended)
- **Storage**: 2GB free space
- **CPU**: Intel Core i3 hoặc AMD equivalent
- **GPU**: DirectX 11 compatible (optional, for better performance)

### Software Dependencies
- **Python 3.8+** với packages: `librosa`, `numpy`, `scipy`
- **FFmpeg** binary trong system PATH

## 🚀 Quick Start

### Option 1: Download Release (Recommended)

1. **Download** latest release từ [Releases page](https://github.com/luyenai/pixbeat5/releases)
2. **Extract** zip file
3. **Install dependencies** (xem [Detailed Installation](#-detailed-installation))
4. **Run** `PixBeat5.exe`

### Option 2: Build from Source

```bash
# Clone repository
git clone https://github.com/luyenai/pixbeat5.git
cd pixbeat5

# Build with .NET 8
dotnet build -c Release

# Run application
dotnet run
```

## 🛠️ Detailed Installation

### Step 1: Install Python Dependencies

```bash
# Option 1: Using pip
pip install librosa numpy scipy

# Option 2: Using conda
conda install -c conda-forge librosa numpy scipy

# Verify installation
python -c "import librosa; print('✓ librosa installed')"
```

### Step 2: Install FFmpeg

#### Windows (Recommended methods):

**Method 1 - Chocolatey:**
```bash
# Install Chocolatey first: https://chocolatey.org/install
choco install ffmpeg
```

**Method 2 - Manual Installation:**
1. Download FFmpeg từ https://www.gyan.dev/ffmpeg/builds/
2. Extract to `C:\ffmpeg`
3. Add `C:\ffmpeg\bin` to system PATH
4. Verify: `ffmpeg -version`

**Method 3 - Winget:**
```bash
winget install Gyan.FFmpeg
```

### Step 3: Verify Installation

```bash
# Check Python
python --version
python -c "import librosa, numpy, scipy; print('✓ All Python packages OK')"

# Check FFmpeg
ffmpeg -version
```

### Step 4: Run PixBeat 5.0

- **From Release**: Double-click `PixBeat5.exe`
- **From Source**: `dotnet run` hoặc F5 trong Visual Studio

## 📖 Usage Guide

### Basic Workflow

#### 1️⃣ Select Audio File
- Click **"Browse Audio File"**
- Supported formats: MP3, WAV, M4A, FLAC
- File size limit: 100MB
- **Tip**: Best results với clear beats và steady tempo

#### 2️⃣ AI Analysis (Automatic)
- AI analyzes audio trong 10-30 giây
- Detects: BPM, Genre, Musical Key, Mood
- Beat detection cho template synchronization
- **Note**: Accuracy tùy thuộc vào audio quality

#### 3️⃣ Choose Template

| Template | Description | Best For |
|----------|-------------|----------|
| **Pixel Runner** | Retro game character nhảy theo beat | Gaming content, nostalgic vibes |
| **Audio Equalizer** | Classic equalizer bars | Electronic music, clean visualization |
| **Waveform Visualizer** | Smooth waveform với effects | Ambient music, professional look |

#### 4️⃣ Configure Settings

**Aspect Ratio:**
- `9:16` - TikTok, Instagram Reels, YouTube Shorts
- `1:1` - Instagram posts, Facebook
- `16:9` - YouTube videos, presentations

**Duration:**
- `15 seconds` - Quick clips
- `30 seconds` - Standard social media
- `60 seconds` - Extended content
- `Full song` - Complete track (use carefully!)

**Quality:**
- `Draft` - Fast render, lower quality (720p)
- `Standard` - Balanced quality/speed (1080p)
- `High` - Best quality, slower render (1080p + higher bitrate)

#### 5️⃣ Generate Video
- Click **"🎬 Generate Video!"**
- Progress bar shows rendering status
- Video saved to `%USERPROFILE%\Videos\`
- Click **"Open Output Folder"** to locate file

### Advanced Tips

#### Getting Better Results

**Audio Selection:**
- ✅ Clear, well-mastered tracks
- ✅ Consistent tempo throughout
- ✅ Strong beat/rhythm
- ❌ Live recordings với audience noise
- ❌ Classical music với tempo changes
- ❌ Very quiet hoặc heavily compressed audio

**Template Selection:**
- **Electronic/Dance** → Audio Equalizer
- **Rock/Pop** → Pixel Runner
- **Ambient/Chill** → Waveform Visualizer

#### Customization Options

**Watermark:**
- Add custom text hoặc leave empty
- Positioned at bottom-right
- Semi-transparent overlay

**Performance Optimization:**
- Close other applications during rendering
- Use SSD for better I/O performance
- Draft quality cho quick previews

## 🎨 Template Details

### Pixel Runner
```json
{
  "style": "8-bit pixel art",
  "elements": ["Running character", "Jumping on beats", "Parallax background", "Energy flashes"],
  "colors": "Retro palette (blues, reds, oranges)",
  "best_for": ["Gaming content", "Upbeat music", "Nostalgic vibes"]
}
```

### Audio Equalizer
```json
{
  "style": "Classic audio visualizer",
  "elements": ["20 frequency bars", "Real-time audio analysis", "Color spectrum"],
  "colors": "Rainbow gradient based on frequency",
  "best_for": ["Electronic music", "Clean visualization", "Professional look"]
}
```

### Waveform Visualizer
```json
{
  "style": "Smooth waveform display",
  "elements": ["Real-time waveform", "Particle effects", "Center timeline"],
  "colors": "Gradient backgrounds with accent colors",
  "best_for": ["Ambient music", "Podcasts", "Smooth visuals"]
}
```

## 🏗️ Technical Architecture

### Project Structure
```
PixBeat5/
├── Models/              # Data models (AudioData, ProjectData, etc.)
├── ViewModels/          # MVVM ViewModels
├── Services/            # Core services (Audio, Render)
├── Views/               # WPF Windows and UserControls
├── Controls/            # Custom WPF controls
├── Templates/           # Template definitions (JSON)
├── Assets/              # Images, icons, resources
├── Python/              # Python scripts for AI analysis
└── docs/               # Documentation and images
```

### Core Technologies
- **Framework**: .NET 8 + WPF
- **UI**: ModernWPF, MVVM pattern
- **Graphics**: SkiaSharp for 2D rendering
- **Audio**: NAudio for basic audio I/O
- **AI Analysis**: Python + librosa
- **Video Export**: FFMpegCore
- **Packaging**: Single-file executable

### Performance Characteristics
- **Frame Generation**: ~30-60 FPS rendering speed
- **Memory Usage**: ~500MB-2GB depending on video length
- **CPU Usage**: Multi-threaded, scales với CPU cores
- **Storage**: Temporary ~1GB per minute of video

## 🔧 Troubleshooting

### Common Issues

#### "Python not found" Error
```bash
# Solution 1: Check Python installation
python --version

# Solution 2: Add Python to PATH
# Windows: System Properties → Environment Variables → PATH

# Solution 3: Install Python from Microsoft Store
# Search "Python" trong Microsoft Store
```

#### "FFmpeg not found" Error
```bash
# Check if FFmpeg is installed
ffmpeg -version

# If not found, install using chocolatey
choco install ffmpeg

# Or add FFmpeg directory to PATH manually
```

#### "librosa import error"
```bash
# Reinstall librosa với dependencies
pip uninstall librosa
pip install librosa

# Or use conda
conda install -c conda-forge librosa
```

#### Slow Rendering Performance
- **Close other applications** to free up CPU/RAM
- **Use Draft quality** cho quick tests
- **Reduce video duration** cho faster renders
- **Check Task Manager** for high CPU usage from other processes

#### Audio Analysis Fails
- **Check audio file format** - MP3, WAV, M4A, FLAC supported
- **Try shorter audio files** - Large files (>100MB) may timeout
- **Ensure audio has clear beats** - Very quiet hoặc ambient tracks may not analyze well

### Log Files and Debugging

**Log Location**: `%TEMP%\PixBeat5\logs\`

**Enable Debug Logging**:
1. Create `appsettings.json` trong app directory:
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug"
    }
  }
}
```

### Getting Help

1. **Check logs** trong temp directory
2. **Try với different audio file** 
3. **Update dependencies** (Python packages, FFmpeg)
4. **Submit issue** với log files và system info

## 🤝 Contributing

We welcome contributions! Here's how to get started:

### Development Setup

1. **Install Visual Studio 2022** với .NET 8.0 workload
2. **Install Python 3.8+** với required packages
3. **Install FFmpeg** và add to PATH
4. **Clone repository**: `git clone https://github.com/luyenai/pixbeat5.git`
5. **Open solution** trong Visual Studio: `PixBeat5.sln`
6. **Build and run**: F5

### Contributing Guidelines

#### Bug Reports
- Use issue template
- Include system info (OS, .NET version, Python version)
- Attach log files if possible
- Steps to reproduce

#### Feature Requests
- Check existing issues first
- Describe use case clearly
- Consider implementation complexity

#### Pull Requests
- Fork repository
- Create feature branch: `git checkout -b feature/new-template`
- Follow C# coding conventions
- Add tests for new features
- Update documentation
- Submit PR với clear description

### Code Style
```csharp
// Use PascalCase for public members
public class AudioAnalysisService
{
    // Use camelCase for private fields
    private readonly ILogger _logger;
    
    // Use async/await pattern
    public async Task<AudioData> AnalyzeAsync(string audioPath)
    {
        // Implementation
    }
}
```

## 📦 Building and Packaging

### Development Build
```bash
dotnet build -c Debug
```

### Release Build
```bash
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

### Creating Installer
```bash
# Using Advanced Installer hoặc similar tool
# Package the published executable với dependencies
```

## 📄 License

This project is licensed under the **MIT License** - see the [LICENSE](LICENSE) file for details.

### Third-Party Licenses
- **ModernWPF**: MIT License
- **SkiaSharp**: MIT License
- **NAudio**: MIT License  
- **FFMpegCore**: MIT License
- **librosa**: ISC License
- **FFmpeg**: LGPL/GPL (binary distribution)

## 🙏 Credits and Acknowledgments

### Development Team
- **Lead Developer**: LuyenAI.vn Team
- **UI/UX Design**: Modern WPF Community
- **Audio Processing**: librosa contributors

### Special Thanks
- **Microsoft** - .NET 8 và WPF framework
- **Skia team** - SkiaSharp graphics library
- **FFmpeg team** - Video encoding capabilities
- **librosa team** - Audio analysis algorithms
- **Community contributors** - Templates, bug reports, suggestions

### Assets and Resources
- **Default templates**: Created by PixBeat team
- **Icons**: Segoe MDL2 Assets font
- **Sample audio**: Royalty-free tracks for testing

## 🔮 Future Roadmap

### Version 5.1 (Planned)
- [ ] More template options (Space, Nature themes)
- [ ] Basic template parameter customization
- [ ] Export to GIF format
- [ ] Drag & drop audio files
- [ ] Built-in audio preview player

### Version 5.2 (Ideas)
- [ ] Simple batch processing
- [ ] Custom color palette selection
- [ ] Basic video effects (fade in/out)
- [ ] Export presets for different platforms
- [ ] Performance optimizations

### Long-term Vision
- Cross-platform support (macOS, Linux)
- Web-based version for mobile devices
- Community template marketplace
- Advanced customization options

## 📞 Support

### Getting Help
- **Documentation**: This README và [Wiki](https://github.com/luyenai/pixbeat5/wiki)
- **Issues**: [GitHub Issues](https://github.com/luyenai/pixbeat5/issues)
- **Community**: [Discussions](https://github.com/luyenai/pixbeat5/discussions)

### Contact Information
- **Website**: https://luyenai.vn
- **Email**: support@luyenai.vn
- **GitHub**: https://github.com/luyenai/pixbeat5

---

<p align="center">
  <img src="docs/images/pixbeat-footer.png" alt="PixBeat" width="200">
  <br>
  <strong>Made with ❤️ by LuyenAI.vn</strong>
  <br>
  <em>Creating pixel perfect music videos, simplified.</em>
</p>

---

**⭐ If you found PixBeat useful, please give us a star on GitHub! ⭐**