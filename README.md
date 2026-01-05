# Busbar CAD - MVP v1.0

A specialized 2D CAD application for designing bent busbars used in electrical switchgear manufacturing.

## Overview

This application replaces manual graph paper drawings with a digital tool that:
- Allows mouse-based drawing of bent busbars
- Automatically calculates bend allowances using K-factor mathematics
- Validates dimensions against manufacturing constraints
- Exports machine-ready .beb files for CNC bending machines

## Features (MVP - Phase 1)

###  Core Functionality
- **Mouse-based Drawing**: Click to create segments, right-click to finish
- **Material Settings Panel**: Configure busbar width, bend radius, K-factor, layer spacing
- **Bend Calculations**: Automatic flat length calculation with configurable K-factor
- **Real-time Validation**: Checks dimensional constraints (50mm/70mm end segments, 80mm middle segments)
- **Visual Feedback**: Blue lines for valid busbars, red for invalid
- **.beb File Export**: Generate CNC machine files for all valid busbars

### Current Limitations (MVP)
- Single layer support only (multi-layer in Phase 2)
- No dimension editing UI yet (Phase 1+)
- No [+] offset tool (Phase 2)
- No clearance management (Phase 2)
- No 3D preview (Phase 4)
- No project save/load (Phase 5)

## Quick Start

### Prerequisites
- Windows 10/11
- .NET 8.0 SDK

### Building
```bash
cd BusbarCAD
dotnet build
```

### Running
```bash
dotnet run
```

Or build and run the executable from:
```
bin\Debug\net8.0-windows\BusbarCAD.exe
```

## Usage

### Drawing a Busbar

1. Click **Draw** button or press **D** key
2. Click on canvas to create points
3. Each click creates a new segment
4. Angles auto-snap to 0°, 90°, 180°, 270° (within ±5°)
5. Right-click or press **ESC** to finish drawing

### Material Settings

- **Busbar Width**: 20-200mm (dropdown)
- **Layer Spacing**: Air gap between layers (default: 20mm)
- **Bend Tool Radius**: CNC tool radius (default: 20mm)
- **K-Factor**: Bend allowance factor (default: 0.45 for aluminum)
- **Thickness**: Fixed at 10mm

### Validation Rules

Busbars must meet these requirements:
- **Both end segments**: ≥ 50mm (absolute minimum)
- **At least one end**: ≥ 70mm (preferred minimum)
- **Middle segments**: ≥ 80mm
- Invalid busbars shown in **red**, valid in **blue**

### Exporting .beb Files

1. Click **Export** button or File → Export .beb Files
2. Select output directory
3. Files generated as: `ProjectName_LayerName_BusbarName.beb`
4. Only valid busbars are exported

## Project Structure

```
BusbarCAD/
├── Models/           # Data classes (Project, Layer, Busbar, Segment, Bend)
├── Calculations/     # BendCalculator, ValidationEngine
├── Export/           # BebFileGenerator
├── UI/               # Dialogs (DimensionDialog)
├── Utilities/        # Helper classes
├── MainWindow.xaml   # Main UI
└── App.xaml          # Application entry
```

## Bend Calculation Mathematics

The application uses industry-standard bend allowance formulas:

### Key Parameters
- **Tool Radius**: Inner bend radius (default: 20mm)
- **K-Factor**: Neutral axis position (default: 0.45 for aluminum)
- **Thickness**: Material thickness (fixed: 10mm)

### Formulas

**Neutral Radius**:
```
Neutral Radius = Tool Radius + (K-Factor × Thickness)
```

**Bend Allowance** (arc length):
```
Arc Length = (Bend Angle / 360°) × 2π × Neutral Radius
```

**Flat Length**:
```
Flat Length = Straight₁ + Arc₁ + Straight₂ + Arc₂ + ... + Straightₙ
```

### Example: L-Bar (100mm × 100mm, 90° bend)

With K=0.45, Tool Radius=20mm, Thickness=10mm:
- Neutral Radius = 20 + (0.45 × 10) = 24.5mm
- Arc Length = (90/360) × 2π × 24.5 = 38.48mm
- Straight sections = 80mm each (accounting for corner cuts)
- **Total Flat Length = 80 + 38.48 + 80 = 198.48mm**

## .beb File Format

The exported .beb files contain:
- Header parameters (flat length, thickness, tool specs)
- Flange definitions (40 entries for segments and angles)
- Line definitions (40 entries for backgauge positions)
- Hole/marking positions (reserved for future use)

Example output snippet:
```
<pl1> 198.484510
<pst> 10.000000
<ptm> MW 90 M
<pts> R 20 CU
<phh> 20.000000
<pfl> 0 0.000000 100.000000
<pfl> 1 90.000000 100.000000
<pln> 0 90.000000 99.242255 0 0.000000 19942
```

## Zoom and Pan Controls

### Mouse Controls
- **Mouse Wheel**: Zoom in/out (centered on cursor position)
- **Middle Mouse Button**: Pan/drag the view
- **Ctrl + Left Mouse Button**: Alternative pan/drag

### Toolbar Buttons
- **Zoom In** (🔍+): Zoom in centered on canvas
- **Zoom Out** (🔍-): Zoom out centered on canvas
- **Fit** (⊡): Auto-scale to fit all busbars in view

### Auto-Scaling
- View automatically scales after each segment is drawn to keep everything visible
- Initial view shows ~200mm of workspace

## Keyboard Shortcuts

- **+** or **=**: Zoom in
- **-** or **_**: Zoom out
- **F**: Fit all busbars in view
- **D**: Start drawing mode
- **ESC**: Cancel current drawing
- **Delete**: Delete selected busbar (when selected in list)

## Known Issues & Warnings

The build produces some warnings:
- Nullable reference warnings (C# 8 strictness - safe to ignore)
- Unused field warnings (reserved for future features)

These do not affect functionality.

## Roadmap

### Phase 2: Layer System & Offset Tool
- Multi-layer support
- [+] button for automatic busbar offsetting
- Calculated offset dimensions based on bend direction

### Phase 3: Validation & Clearance
- Enhanced red highlighting for invalid segments
- Clearance tool for air gaps
- Dynamic dimension linking and propagation

### Phase 4: 3D Visualization
- Isometric 3D preview
- Visual clearance checking
- Interactive rotation/zoom

### Phase 5: Polish & Features
- Project save/load (JSON format)
- K-factor calibration wizard
- Dimension editing dialog
- Hole/marking tool
- Cut list reports
- Undo/redo
- Templates & busbar library

## Technical Details

- **Framework**: .NET 8.0, WPF (Windows Presentation Foundation)
- **Language**: C# 12
- **UI**: XAML markup with code-behind
- **Graphics**: WPF Canvas with Line shapes
- **File I/O**: System.IO, Newtonsoft.Json

## Support

For issues or questions, refer to the specification document:
`busbar_cad_specification.md`

## License

Built with Claude Code - December 2024

---

**Version**: 1.0 MVP
**Status**: Fully functional Phase 1 implementation
**Next Steps**: Test with real busbars, then proceed to Phase 2
