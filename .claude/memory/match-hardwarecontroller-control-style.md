---
name: match-hardwarecontroller-control-style
description: "HC2's controls should look like the old HardwareController's — stock checkboxes, 4px accent buttons."
metadata: 
  node_type: memory
  type: feedback
  originSessionId: 25e156f6-51b0-459c-8925-7291a248111a
  modified: 2026-09-09T21:38:41.859Z
---

For HC2's UI, match the visual language of the old WPF HardwareController at
`C:\. Projects\HardwareController\UI\Styles\AppStyles.xaml` rather than inventing a new one. Asked for
checkboxes and buttons specifically: the stock WPF checkbox (white square, dark tick — no custom
accent-filled box) and buttons with a 4px corner radius, 30px body, `#2D6CDF` accent fill for primary and a
subtle grey fill for secondary.

**Why:** HC2 is a rewrite of that tool ([[hc2-is-hardwarecontroller-rewrite]]) and the two sit on the same
bench; controls that differ read as two different products.

**How to apply:** before restyling anything in HC2, open that AppStyles.xaml and copy the corresponding
style. HC2's own dictionary is `src/HC2.App/Theme.xaml`.
