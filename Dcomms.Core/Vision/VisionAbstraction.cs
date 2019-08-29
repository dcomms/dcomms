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
        public virtual AttentionLevel GetAttentionTo(string visionChannelSourceId, string moduleName) => AttentionLevel.deepDetail;
        public abstract void Emit(string visionChannelSourceId, string moduleName, AttentionLevel level, string message);
        public virtual void Emit(string visionChannelSourceId, string moduleName, double value, double? lightPainThresholdL, double? mediumPainThresholdL)
        {
            if (value > mediumPainThresholdL)
            {
                if (GetAttentionTo(visionChannelSourceId, moduleName) <= AttentionLevel.mediumPain)
                    Emit(visionChannelSourceId, moduleName, AttentionLevel.mediumPain, $"value={value} is above threshold {mediumPainThresholdL}");
            }
            else if (value > mediumPainThresholdL)
            {
                if (GetAttentionTo(visionChannelSourceId, moduleName) <= AttentionLevel.lightPain)
                    Emit(visionChannelSourceId, moduleName, AttentionLevel.lightPain, $"value={value} is above threshold {lightPainThresholdL}");
            }
            else
            {
                if (GetAttentionTo(visionChannelSourceId, moduleName) <= AttentionLevel.detail)
                    Emit(visionChannelSourceId, moduleName, AttentionLevel.detail, $"value={value}");
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
        public override void Emit(string visionChannelSourceId, string moduleName, AttentionLevel level, string message)
        {
            _wtl($"[{visionChannelSourceId}] {message}");
        }
    }
}
