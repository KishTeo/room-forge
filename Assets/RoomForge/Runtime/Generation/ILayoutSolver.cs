using System;
using System.Collections.Generic;

namespace RoomForge
{
    public interface ILayoutSolver
    {
        void Solve(List<RoomInstance> rooms, DungeonConfigSO config, Random random);
    }
}
