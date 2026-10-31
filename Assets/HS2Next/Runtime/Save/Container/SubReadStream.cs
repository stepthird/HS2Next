// A read-only window [start, start + length) of a seekable stream, so an entry's compressed bytes
// can be inflated without the inflater reading past them (and the relative-offset form
// step 3 can be addressed from its base).
using System;
using System.IO;

namespace HS2Next.Save
{
    sealed class SubReadStream : Stream
    {
        readonly Stream inner;
        readonly long start, length;
        long pos;

        public SubReadStream(Stream inner, long start, long length)
        {
            this.inner = inner;
            this.start = start;
            this.length = length;
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => length;

        public override long Position
        {
            get => pos;
            set => pos = Math.Max(0, Math.Min(value, length));
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            long left = length - pos;
            if (left <= 0) return 0;
            if (count > left) count = (int)left;
            inner.Position = start + pos;
            int n = inner.Read(buffer, offset, count);
            pos += n;
            return n;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            Position = origin == SeekOrigin.Begin ? offset : origin == SeekOrigin.Current ? pos + offset : length + offset;
            return pos;
        }

        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
