// Typed errors of the HS2 pose-file reader. Every failure of
// PoseFile.Read / ReadFile / TryRead is a PoseFormatException with a code and the absolute byte offset in the
// file where reading stopped; no other exception type leaves the reader. Unity-free (System only).
using System;

namespace HS2Next.Import.Pose
{
    /// <summary>Why a file is not a readable pose.</summary>
    public enum PoseErrorCode
    {
        /// <summary>The identifying string is not `【pose】` (a `.dat` file of another kind).</summary>
        BadMagic,
        /// <summary>The file ends inside a field (offset = where the field starts).</summary>
        Truncated,
        /// <summary>An IK or FK count is negative or needs more bytes than the file has (offset = the count field).</summary>
        CountOutOfRange,
        /// <summary>A 7-bit string length is malformed (more than 5 bytes, negative) or runs past the end of the file.</summary>
        BadString,
        /// <summary>A PNG container whose chunk list is broken before IEND (offset = the chunk).</summary>
        BadPng,
        /// <summary>A PNG that ends at IEND, or whose bytes after IEND are not a pose: "not a pose", not an error of the library.</summary>
        NoPayload,
        /// <summary>The file could not be read (I/O); offset -1.</summary>
        Io,
        /// <summary>A reader defect caught defensively (should never occur; reported instead of an unhandled exception).</summary>
        Internal,
    }

    /// <summary>A pose file that cannot be read, with the absolute byte offset where reading stopped.</summary>
    public sealed class PoseFormatException : Exception
    {
        public PoseErrorCode Code { get; }
        /// <summary>Absolute byte offset in the file (PNG: counted from the start of the image), or -1.</summary>
        public long Offset { get; }

        public PoseFormatException(PoseErrorCode code, long offset, string message, Exception inner = null)
            : base(code + " at " + offset + ": " + message, inner)
        {
            Code = code;
            Offset = offset;
        }
    }
}
