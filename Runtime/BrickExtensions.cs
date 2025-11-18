// Copyright (C) LEGO System A/S - All Rights Reserved
// Unauthorized copying of this file, via any medium is strictly prohibited
// Place this file in: Runtime/BrickExtensions.cs (NOT in Editor folder)

using UnityEngine;
using System.Collections.Generic;

namespace LEGOModelImporter
{
    /// <summary>
    /// Extension methods for Brick class to support decoupling
    /// </summary>
    public static class BrickExtensions
    {
        /// <summary>
        /// Disconnects a brick from all connected bricks using the existing DisconnectInverse API
        /// </summary>
        /// <param name="brick">The brick to disconnect</param>
        public static void DisconnectAll(this Brick brick)
        {
            if (brick == null) return;

            // Use the existing DisconnectInverse method with an empty collection
            // This will disconnect the brick from all other bricks
            brick.DisconnectInverse(new Brick[0]);
        }
    }
}