# Languages and Accessibility

## Languages

Both applications support:

- English
- Polish

Android also offers **System** language behavior. WPF can switch English/Polish at runtime from the main window.

English is the canonical documentation language for the GitHub repository.

## Android

The Compose UI is designed to support:

- TalkBack semantic labels
- explicit text status (`Ready`, `Action required`, `Problem`, `Unverified`) in addition to symbols
- progress semantics for readiness
- minimum 48 dp primary interaction targets
- large font scaling without fixed-height text containers
- logical Back navigation
- no status communicated only through color

Device/OEM accessibility behavior should still be validated on real phones before Stable.

## Windows Builder

The WPF interface includes:

- AutomationProperties labels on key controls
- keyboard-focusable controls and access-key markers
- runtime PL/EN ResourceDictionary switching
- normal WPF DPI scaling
- no custom color-only state widgets
- compatibility with Windows high-contrast rendering for standard controls

Before Stable, test with Windows Narrator, 150–200% scaling and keyboard-only navigation.
