using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;

namespace _16pin_vmon.ViewModels;

public partial class DisclaimerViewModel : ViewModelBase
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AcceptCommand))]
    private bool _isDisclaimerAccepted;

    public event Action? OnAccepted;
    public event Action? OnDeclined;

    public string DisclaimerTitle => "⚠️ 法律免責聲明 / Legal Disclaimer";

    public string DisclaimerText => """
        本軟體係依「現狀」(AS IS) 提供，開發者不負任何形式的擔保責任。

        使用者應了解，本工具涉及對顯卡底層硬體 (NVML) 的存取與監測。在任何情況下，開發者對於因使用或無法使用本軟體而產生的任何直接、間接、附帶、特別、懲罰性或衍生性損害（包括但不限於：硬體熔毀、數據損失、系統損壞或營收損失）均不負任何賠償責任。

        THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED. IN NO EVENT SHALL THE AUTHORS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
        """;

    public string CheckboxText => "我已閱讀免責聲明，並了解監控高負載硬體之風險";

    [RelayCommand(CanExecute = nameof(CanAccept))]
    private void Accept()
    {
        OnAccepted?.Invoke();
    }

    private bool CanAccept() => IsDisclaimerAccepted;

    [RelayCommand]
    private void Decline()
    {
        OnDeclined?.Invoke();
    }
}
