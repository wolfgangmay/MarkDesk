# MarkDesk — Project Rules

.NET 10 WPF Markdown editor (AvalonEdit + WebView2 preview + Markdig).
Build: `dotnet build MarkDesk.slnx -c Release` · Tests: `dotnet test MarkDesk.slnx`

## HARD RULE: UI must follow the active theme

**Every new or modified UI element MUST render correctly in BOTH light and dark
themes.** This is non-negotiable. A control that only looks right in one theme
is a bug (e.g. the Scroll-sync CheckBox once used the default WPF template and
showed system-fixed colors in dark mode).

Rules:

1. **Never hardcode colors** in controls (`Background="#FFF"`, `Foreground="Black"`,
   …). Always reference the app brushes via `{DynamicResource …}`:
   `WindowBgBrush`, `BarBgBrush`, `ToneBgBrush`, `ContentBrush`, `MutedBrush`,
   `DividerBrush`, `HoverBrush`, `PressedBrush`, `AccentBrush`,
   `AccentSoftBrush`, `AccentTextBrush`, `ScrollThumbBrush`,
   `ScrollThumbHoverBrush` (declared in `App.xaml`, switched at runtime by
   `MainWindow.ApplyTheme`).
   - Exception: pure white/black on an already-accented surface (e.g. the check
     glyph inside an `AccentBrush`-filled box).

2. **Never rely on default WPF templates for interactive controls.**
   `CheckBox`, `RadioButton`, `ToggleButton` etc. use theme-chrome with fixed
   system colors for their states. Use / extend the themed styles instead:
   `ThemedCheckBox`, `ThemedButton`, `ThemedAccentButton`, `ThemedTextBox`,
   `ThemedComboBox` (App.xaml) or provide a ControlTemplate whose every visual
   state (normal / hover / checked / pressed / disabled) binds to the app
   brushes above. See `ThemedCheckBox` in `App.xaml` for the reference pattern.

3. **Adding a new theme-dependent brush** = two mandatory edits:
   a default `<SolidColorBrush>` in `App.xaml` **and** assignments in **both**
   branches (dark + light) of `MainWindow.ApplyTheme`. Missing one branch means
   the brush silently keeps the other theme's value.

4. **Verify visually in both themes** before considering a UI change done:
   flip Settings → Appearance → Theme (Light/Dark) and check hover, checked,
   and disabled states of what you touched.
