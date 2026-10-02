# ui

Shared design tokens and components, used by all three Next.js apps —
portaladmin, portalweb, portalforms.

There is no package here. Each app imports this directory by relative path
(`../../libs/ui/tokens.css` and so on) rather than through `node_modules`,
so a change here takes effect in every app without a publish step.

`tokens.css` is the palette. Import it; never write a hex in an app.

The announcement reaction picker is inspired by [Rare UI's Emoji reaction](https://www.rareui.com/components/emojireaction), using native emoji, the shared MorganHacks palette, and persisted audience reactions.
