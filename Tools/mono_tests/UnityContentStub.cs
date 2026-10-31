// The two UnityEngine types the zipmod scan's sources name (Runtime/Content/IContentSource.cs, ZipmodInfo.cs,
// ZipmodSourceBase.cs), for the plain-Mono content suite (Tools/mono_tests.ps1 -Suite content). Opening a bundle needs Unity:
// every member throws.
using System;

namespace UnityEngine
{
    public class Object
    {
        public static implicit operator bool(Object o) => o != null;
    }

    public sealed class AssetBundle : Object
    {
        public static AssetBundle LoadFromFile(string path) => throw new NotSupportedException("no Unity in the plain-Mono content suite");
        public static AssetBundle LoadFromFile(string path, uint crc, ulong offset) => throw new NotSupportedException("no Unity in the plain-Mono content suite");
        public static AssetBundle LoadFromMemory(byte[] binary) => throw new NotSupportedException("no Unity in the plain-Mono content suite");
    }
}
