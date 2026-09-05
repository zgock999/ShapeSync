// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using zgock.ShapeSync.Editor;
using UnityEngine;

namespace zgock.ShapeSync.Tests.EditMode
{

    public sealed class SafeWavefrontTransferTests
    {
        [Test]
        public void SWT01_UnitScales_MatchDazAndMeasuredPoserContracts()
        {
            Assert.That(ExternalTransferWindow.GetExportPositionScale(ExternalTransferWindow.ExternalUnitSystem.UnityMeters), Is.EqualTo(1f));
            Assert.That(ExternalTransferWindow.GetExportPositionScale(ExternalTransferWindow.ExternalUnitSystem.DazStudioCentimeters), Is.EqualTo(100f));
            Assert.That(ExternalTransferWindow.GetExportPositionScale(ExternalTransferWindow.ExternalUnitSystem.PoserEightFeet), Is.EqualTo(1f / 2.4384f).Within(0.0000001f));
            Assert.That(ExternalTransferWindow.GetImportPositionScale(ExternalTransferWindow.ExternalUnitSystem.PoserEightFeet), Is.EqualTo(2.4384f).Within(0.000001f));
        }

        [Test]
        public void SWT02_ImportThreshold_PreservesOnlySubThresholdSourcePositions()
        {
            Mesh source = new Mesh();
            string path = Path.Combine(Path.GetTempPath(), "ShapeSync_SafeWavefrontTransfer_" + Guid.NewGuid().ToString("N") + ".obj");
            try
            {
                source.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
                source.triangles = new[] { 0, 1, 2 };
                Vector3[] externalPositions =
                {
                    new Vector3(0.00005f, 0f, 0f),
                    new Vector3(1.0002f, 0f, 0f),
                    Vector3.up
                };
                SafeWavefrontTransfer.Write(source, externalPositions, null, path);

                Assert.That(SafeWavefrontTransfer.TryRead(path, source, true, 1f, 0.0001f, out Vector3[] positions, out _, out string error), Is.True, error);
                Assert.That(positions[0], Is.EqualTo(Vector3.zero));
                Assert.That(positions[1].x, Is.EqualTo(1.0002f).Within(0.0000001f));
                Assert.That(positions[2], Is.EqualTo(Vector3.up));
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
                UnityEngine.Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void SWT03_ExportFaces_ReconstructsAdjacentTrianglePairAsQuadWithoutChangingVertexIndices()
        {
            List<int[]> faces = SafeWavefrontTransfer.BuildExportFaces(new[]
            {
                0, 1, 2,
                2, 1, 3
            });

            Assert.That(faces, Has.Count.EqualTo(1));
            Assert.That(faces[0], Has.Length.EqualTo(4));
            Assert.That(faces[0], Is.EquivalentTo(new[] { 0, 1, 2, 3 }));
        }

        [Test]
        public void SWT04_ExportFaces_LeavesUnpairedTriangleAsTriangle()
        {
            List<int[]> faces = SafeWavefrontTransfer.BuildExportFaces(new[] { 0, 1, 2 });

            Assert.That(faces, Has.Count.EqualTo(1));
            Assert.That(faces[0], Is.EqualTo(new[] { 0, 1, 2 }));
        }
    }

}
