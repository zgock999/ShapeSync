// SPDX-License-Identifier: MIT
// Copyright (c) 2026 zgock999

using System.Collections.Generic;
using zgock.ShapeSync.StackMachine;

namespace zgock.ShapeSync
{
    /// <summary>Serializes a runtime Shape list to an implementation-defined file name.</summary>
    public interface IShapeSerializer
    {
        /// <summary>Serializes the supplied logical runtime Shapes to the supplied file name.</summary>
        /// <param name="fileName">Implementation-defined destination identifier.</param>
        /// <param name="runtimeShapes">The Director logical-state snapshot to store.</param>
        /// <returns><see langword="true"/> when the destination accepted the complete Shape list.</returns>
        bool TrySerialize(string fileName, List<ShapeSyncShape> runtimeShapes);
    }

    /// <summary>Deserializes an implementation-defined file name into a runtime Shape list.</summary>
    public interface IShapeDeserializer
    {
        /// <summary>Deserializes logical runtime Shapes from the supplied file name.</summary>
        /// <param name="fileName">Implementation-defined source identifier.</param>
        /// <param name="runtimeShapes">The decoded detached logical Shapes on success.</param>
        /// <returns><see langword="true"/> when the source was decoded successfully.</returns>
        bool TryDeserialize(string fileName, out List<ShapeSyncShape> runtimeShapes);
    }

    /// <summary>Optional capability for decoding a standard in-memory <see cref="ShapeDocument"/> source.</summary>
    /// <remarks>
    /// <see cref="ShapeDirector"/> uses this capability only through its configured
    /// <see cref="IShapeDeserializer"/>.  The Director does not decode ShapeDocument records itself,
    /// so custom serialization formats retain ownership of their input representation.
    /// </remarks>
    public interface IShapeDocumentSourceDeserializer
    {
        /// <summary>Decodes the supplied standard carrier into detached logical Shapes and its recipe snapshot.</summary>
        /// <param name="source">The caller-owned standard document asset.</param>
        /// <param name="runtimeShapes">Decoded detached logical Shapes on success.</param>
        /// <param name="payload">Detached Mesh and Material recipe snapshot on success.</param>
        /// <param name="diagnostic">A structured source decoding or snapshot failure.</param>
        /// <returns><see langword="true"/> when the source was decoded without using an asset path.</returns>
        bool TryDeserialize(ShapeDocument source, out List<ShapeSyncShape> runtimeShapes, out ShapeSyncDocument payload, out StackMachineDiagnostic diagnostic);
    }
}
