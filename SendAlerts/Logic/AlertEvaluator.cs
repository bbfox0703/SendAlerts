using System.Collections.Generic;
using System.Linq;

namespace SendAlerts.Logic;

public class AlertEvaluator
{
    private readonly Queue<float> _history = new();
    private readonly int _maxSeconds;
    private readonly int _thresholdCount;
    private readonly float _thresholdValue;
    private readonly bool _isLowerBound; // 電壓是低於觸發，溫度是高於觸發

    public AlertEvaluator(int seconds, int count, float threshold, bool isLowerBound = false)
    {
        _maxSeconds = seconds;
        _thresholdCount = count;
        _thresholdValue = threshold;
        _isLowerBound = isLowerBound;
    }

    public bool PushValueAndCheckAlert(float value)
    {
        _history.Enqueue(value);
        
        // 維持滑動視窗大小 (假設一秒一筆)
        while (_history.Count > _maxSeconds)
            _history.Dequeue();

        // 判定邏輯
        int violations = _isLowerBound 
            ? _history.Count(v => v <= _thresholdValue) 
            : _history.Count(v => v >= _thresholdValue);

        return violations >= _thresholdCount;
    }
}