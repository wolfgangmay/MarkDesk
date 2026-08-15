# Link & Anchor Test

This file exercises the preview's link rules. Click every link below and check the behavior:

- **Valid anchors** scroll smoothly inside the page (no dialog).
- **Broken anchor / external / relative / email links** are blocked: a warning dialog appears and the preview page is never left.

## Table of contents

- [Valid anchor -> Section One](#section-one)
- [Valid anchor -> Section Two](#section-two)
- [Valid anchor -> Section Three](#three-contains-a-nested-list)
- [Valid CJK anchor -> 中文标题](#中文标题)
- [Valid CJK anchor (encoded href) -> 中文 标题](#中文%20标题)
- [Broken anchor](#no-such-section-exists)
- [External link: GitHub](https://github.com/wolfgangmay/MarkDesk)
- [Relative file link](links-target.md)
- [Email link](mailto:test@example.com)
- [Bare anchor link](#)

## Section One

Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. Ut enim ad minim veniam, quis nostrud exercitation ullamco.

> Back to [top](#link--anchor-test) — this anchor scrolls up to the H1.

## Section Two

Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris nisi ut aliquip ex ea commodo consequat. Duis aute irure dolor in reprehenderit.

## 中文标题

中文锚点测试:此标题验证非 ASCII 锚点(百分号编码的 href)能正确跳转,不应弹出错误对话框。

## 中文 标题

带空格的中文锚点测试:同样应正确跳转。

## Section Three

This heading demonstrates a nested item list under section two:

- Item alpha
- Item beta
  - Item beta-1
  - Item beta-2 (still part of Section Three)

[Back to top](#link--anchor-test)

## What happens on a missing anchor?

Return to the [table of contents](#table-of-contents) and click the *broken anchor* entry. The preview should:

1. Not navigate anywhere (no WebView error page).
2. Show a dialog: `The anchor '#no-such-section-exists' does not exist on this page.`
3. Close the dialog and the rendered page remains intact.

## What happens on non-anchor links?

The [external](https://github.com/wolfgangmay/MarkDesk), [relative](links-target.md) and [mail](mailto:test@example.com) links are all blocked with the same warn-and-stay behavior.