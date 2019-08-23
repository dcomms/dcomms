using Dcomms.CCP;
using System;
using System.Collections.Generic;
using System.Text;

namespace Dcomms.Vision
{
    public enum AttentionLevel
    {
        /// <summary>
        /// details that can easily overload the RAM (sent every 10ms)
        /// </summary>
        deepDetail,
        /// <summary>
        /// "sent request X" "received response X totally 10 times"
        /// </summary>
        detail,
        /// <summary>
        /// "pressed button X", "started app", "closed app after 10 minutes"
        /// </summary>
        guiActivity,
        /// <summary>
        /// (possible) problem expressed by user: closed app with no clicks, sent a bad feedback "app is not clear" "app does not work"
        /// </summary>
        guiPain,
        /// <summary>
        /// signal of possible future problems, self-tested in code
        /// "abnormal delay in X"  
        /// </summary>
        lightPain,
        /// <summary>
        /// self-tested problem; an important problem but the application still works and is able to provide some value to user
        /// </summary>
        mediumPain,
        strongPain,
        /// <summary>
        /// application crashed after non-recoverable error,  like memory leak
        /// </summary>
        death
    }

    /// <summary>
    /// provides link from executing code to developer
    /// sends various signals to developer via CCP, via GUI display, via log files
    /// 
    /// (optionally) relies on CCP
    /// </summary>
    public abstract class VisionChannel
    {
        public virtual AttentionLevel GetAttentionTo(string objectName, string sourceCodePlaceId) => AttentionLevel.deepDetail;
        public abstract void Emit(string objectName, string sourceCodePlaceId, AttentionLevel level, string message);
        public virtual void Emit(string objectName, string sourceCodePlaceId, double value, double? lightPainThresholdL, double? mediumPainThresholdL)
        {
            if (value > mediumPainThresholdL)
            {
                if (GetAttentionTo(objectName, sourceCodePlaceId) <= AttentionLevel.mediumPain)
                    Emit(objectName, sourceCodePlaceId, AttentionLevel.mediumPain, $"value={value} is above threshold {mediumPainThresholdL}");
            }
            else if (value > mediumPainThresholdL)
            {
                if (GetAttentionTo(objectName, sourceCodePlaceId) <= AttentionLevel.lightPain)
                    Emit(objectName, sourceCodePlaceId, AttentionLevel.lightPain, $"value={value} is above threshold {lightPainThresholdL}");
            }
            else
            {
                if (GetAttentionTo(objectName, sourceCodePlaceId) <= AttentionLevel.detail)
                    Emit(objectName, sourceCodePlaceId, AttentionLevel.detail, $"value={value}");
            }
        }
    }
    public class SimplestVisionChannel : VisionChannel
    {
        readonly Action<string> _wtl;
        public SimplestVisionChannel(Action<string> wtl)
        {
            _wtl = wtl;
        }
        public override void Emit(string objectName, string sourceCodePlaceId, AttentionLevel level, string message)
        {
            _wtl($"{sourceCodePlaceId} {message}");
        }
    }
}
