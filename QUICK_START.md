# Busbar CAD - Quick Start Guide

## First Time Running

1. **Open the application**
   - Double-click `BusbarCAD.exe` in `bin\Debug\net8.0-windows\`
   - Or run `dotnet run` from the project folder

2. **You'll see**:
   - Left panel: Material settings
   - Center: White drawing canvas with instructions
   - Top: Menu bar and toolbar with Draw button

## Drawing Your First Busbar

### Example: Simple L-Shape (100mm × 100mm)

1. **Click "Draw" button** (or press D key)
   - Status bar shows: "Drawing mode: Click to add points..."

2. **Click Point 1** anywhere on canvas (starting point)
   - Example: Click at position (200, 300)

3. **Click Point 2** to create first segment
   - Move mouse 100 pixels to the right
   - Example: Click at (300, 300)
   - You'll see a blue line appear

4. **Click Point 3** to create bend and second segment
   - Move mouse 100 pixels down (creates 90° bend)
   - Example: Click at (300, 400)
   - Another blue line appears

5. **Right-click** to finish
   - Busbar appears in the list on the left
   - Status bar shows flat length: ~198.48mm
   - Validation status shows "All busbars valid"

## Understanding the Result

The busbar you just drew:
- **Inside dimensions**: 100mm × 100mm (as drawn)
- **Bend angle**: 90° (automatically detected)
- **Flat length**: 198.48mm (calculated with K-factor 0.45)

## Try Different Shapes

### U-Shape (80mm × 100mm × 120mm)

1. Press **D** to start drawing
2. Click 4 points:
   - Point 1: (200, 200)
   - Point 2: (280, 200)   ← 80mm horizontal
   - Point 3: (280, 300)   ← 100mm down (90° bend)
   - Point 4: (400, 300)   ← 120mm right (90° bend)
3. Right-click to finish

You should see:
- Flat length: ~298.96mm
- 2 bends detected
- Blue lines (valid)

### Straight Bar (150mm)

1. Press **D**
2. Click 2 points:
   - Point 1: (100, 100)
   - Point 2: (250, 100)
3. Right-click

Result:
- Flat length: 150mm (no bends)
- Always valid (single segment has no restrictions)

## Testing Validation

### Create an INVALID busbar

Try a busbar with ends that are too short:

1. Press **D**
2. Draw a small U-shape:
   - Point 1: (200, 200)
   - Point 2: (240, 200)   ← 40mm (too short!)
   - Point 3: (240, 250)
   - Point 4: (300, 250)   ← 60mm
3. Right-click

You should see:
- **Red lines** (invalid)
- Validation status shows errors:
  - "First segment (40mm) is below absolute minimum (50mm)"
  - "Neither end segment meets preferred minimum (70mm)"

## Exporting to .beb Files

### Export your valid busbars

1. Click **Export** button (or File → Export .beb Files)
2. Browse to a folder (e.g., Desktop or Documents)
3. Click OK

You should see:
- "Successfully exported N busbar(s) to: [path]"
- Files created: `Untitled Project_Layer 1_Bar 1.beb`, etc.

### View the .beb file

Open a .beb file in Notepad to see the machine code:

```
<pl1> 198.484510          ← Total flat length
<pst> 10.000000           ← Thickness
<ptm> MW 90 M             ← Tool specification
<pts> R 20 CU             ← Punch tool
<phh> 20.000000           ← Bend radius
<mat> 6 Aluminium         ← Material
<pfl> 0 0.000000 100.000000    ← First segment
<pfl> 1 90.000000 100.000000   ← Second segment with 90° bend
<pln> 0 90.000000 99.242255... ← Backgauge position
```

## Adjusting Settings

### Change Busbar Width

1. In left panel, click "Busbar Width" dropdown
2. Select 100mm (instead of default 80mm)
3. This affects layer height calculations (for future multi-layer support)

### Change K-Factor

1. In left panel, find "K-Factor" textbox
2. Change from 0.45 to 0.38 (for copper instead of aluminum)
3. Draw a new busbar
4. Notice the flat length is different!

**Example**: Same 100×100 L-bar with K=0.38 → Flat length ≈ 197.92mm

### Change Bend Tool Radius

1. Change "Bend Tool Radius" from 20mm to 30mm
2. Draw a new busbar
3. Larger radius = longer arc = longer flat length

## Tips & Tricks

### Auto-Snapping
- Angles within ±5° of 0°, 90°, 180°, 270° snap automatically
- Hold SHIFT to disable snapping (future feature)

### Precision
- The grid is visual-only in MVP
- Use coordinates in status bar to track positions
- Each pixel ≈ 1mm for rough estimation

### Deleting Busbars
1. Click busbar in the list (left panel)
2. Edit → Delete Busbar
3. Or just draw a new project (File → New Project)

### Keyboard Shortcuts
- **D** = Start drawing
- **ESC** = Cancel current drawing (before right-click)
- **Delete** = Delete selected busbar
- **+** or **=** = Zoom in
- **-** or **_** = Zoom out
- **F** = Fit all busbars in view

### Zoom and Pan
- **Mouse Wheel** = Zoom in/out (towards cursor)
- **Middle Mouse Button** = Pan/drag the view
- **Ctrl + Left Mouse** = Alternative pan
- **Toolbar Buttons** = Zoom In, Zoom Out, Fit
- **Auto-scaling** = View automatically adjusts after each segment to keep everything visible

## Common Questions

### Q: Why is my busbar red?
**A**: It violates validation rules. Check:
- Both ends ≥ 50mm?
- At least one end ≥ 70mm?
- All middle segments ≥ 80mm?

### Q: What's the flat length?
**A**: The total length of the flat bar before bending. This is what you cut from stock material.

### Q: Can I edit dimensions after drawing?
**A**: Not in MVP (Phase 1). Coming in Phase 1+ with dimension dialog.

### Q: Can I save my project?
**A**: Not in MVP. Coming in Phase 5. For now, just export .beb files.

### Q: Can I have multiple layers?
**A**: Not in MVP (single layer only). Multi-layer support coming in Phase 2.

### Q: My busbar has a weird angle (like 87°)
**A**: Draw more precisely or wait for angle snapping improvements. 87° should snap to 90° if within 5°.

## Testing the Math

### Verify the calculations are correct

Create a known test case:
- **Inside dimensions**: 100mm × 100mm
- **90° bend**
- **K-factor**: 0.45
- **Tool radius**: 20mm
- **Thickness**: 10mm

**Expected flat length**: 198.48mm

From specification:
```
Neutral radius = 20 + (0.45 × 10) = 24.5mm
Arc = (90/360) × 2π × 24.5 = 38.48mm
Straight sections = 80mm each
Total = 80 + 38.48 + 80 = 198.48mm ✓
```

If your result matches → Calculations are correct!

## Next Steps

Once you're comfortable with MVP:

1. **Test with real busbars**: Compare to actual machine results
2. **Calibrate K-factor**: Use calibration wizard (Phase 5) or manually adjust
3. **Request Phase 2**: Multi-layer support and [+] offset tool
4. **Provide feedback**: What features do you need most?

## Troubleshooting

### Application won't start
- Check .NET 8.0 is installed: `dotnet --version`
- Run from terminal to see error messages

### Can't click Draw button
- Make sure a layer exists (should have "Layer 1" by default)

### Export doesn't create files
- Check folder permissions
- Try exporting to Desktop first

### Lines don't appear when drawing
- Canvas might be zoomed/panned (not implemented yet)
- Try File → New Project to reset

---

**Have fun designing busbars!**

For detailed information, see `README.md` and the full specification document.
