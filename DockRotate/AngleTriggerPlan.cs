using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DockRotate
{
    public enum AngleTriggerPlan
    {
        WaitingForMaxSpeed = 0,
        MaxSpeedAchieved = 1,
        CalculatingLaunchPlan = 2,
        CountdownStarted = 3,
        Fire = 4
    }
}
