#nullable disable
using System.Windows.Controls;

namespace FieldTakHub.Builder.Views
{
    public partial class FactionPackageBuilderView : UserControl
    {
        public void OnLanguageChanged() => RebuildView();
        partial void RebuildView();
    }

    public partial class TacticalMapStudioView : UserControl
    {
        public void OnLanguageChanged() => RebuildView();
        partial void RebuildView();
    }

    public partial class OpordGeneratorView : UserControl
    {
        public void RefreshFromStore() => RebuildView();
        public void OnLanguageChanged() => RebuildView();
        partial void RebuildView();
    }

    public partial class UsbProgrammerView : UserControl
    {
        public void RefreshFromStore() => RebuildView();
        public void OnLanguageChanged() => RebuildView();
        partial void RebuildView();
    }
}
