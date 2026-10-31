// LZMA decoder for the LZMA blocks of UnityFS bundles (v0.3 trial 2, package G): a block is the 5-byte properties header
// (lc / lp / pb in one byte, the dictionary size in four little-endian bytes) and the raw LZMA stream, whose uncompressed
// size the bundle's block table gives. Written from the format's published description (the LZMA SDK's specification);
// decoding only, into a buffer of exactly the expected size; a malformed stream throws Hs2SerializedFileException. Used by
// Hs2BundleFile when a caller asks for LZMA support (the thumbnail header check reads LZMA thumbnail bundles too).
using System;

namespace HS2Next.Content
{
    public static class Hs2Lzma
    {
        /// <summary>Decodes one Unity LZMA block into exactly <paramref name="outSize"/> bytes.</summary>
        public static byte[] Decode(byte[] src, int outSize)
        {
            if (src == null || src.Length < 5) throw Bad("block shorter than its properties");
            int d = src[0];
            if (d >= 9 * 5 * 5) throw Bad("bad properties byte " + d);
            int lc = d % 9;
            d /= 9;
            int lp = d % 5, pb = d / 5;
            var dec = new Decoder(src, 5, lc, lp, pb, outSize);
            return dec.Run();
        }

        static Hs2SerializedFileException Bad(string why) => new Hs2SerializedFileException("lzma: " + why);

        sealed class Decoder
        {
            const int States = 12, PosBitsMax = 4, LenToPosStates = 4, AlignBits = 4, EndPosModelIndex = 14, FullDistances = 128;
            const uint TopValue = 1u << 24;

            readonly byte[] src;
            int sp;
            uint range = 0xFFFFFFFF, code;
            readonly byte[] outBuf;
            int op;
            readonly int lc, lp, pb;

            readonly ushort[] isMatch = Probs(States << PosBitsMax), isRep = Probs(States), isRepG0 = Probs(States), isRepG1 = Probs(States),
                isRepG2 = Probs(States), isRep0Long = Probs(States << PosBitsMax), posSlot = Probs(LenToPosStates << 6),
                posDecoders = Probs(1 + FullDistances - EndPosModelIndex), align = Probs(1 << AlignBits), literals;
            readonly LenDecoder lenDec = new LenDecoder(), repLenDec = new LenDecoder();

            static ushort[] Probs(int n)
            {
                var p = new ushort[n];
                for (int i = 0; i < n; i++) p[i] = 1024;
                return p;
            }

            public Decoder(byte[] src, int start, int lc, int lp, int pb, int outSize)
            {
                this.src = src;
                sp = start;
                this.lc = lc; this.lp = lp; this.pb = pb;
                outBuf = new byte[outSize];
                literals = Probs(0x300 << (lc + lp));
                if (Next() != 0) throw Bad("the range coder's first byte is not 0");
                for (int i = 0; i < 4; i++) code = (code << 8) | Next();
                if (code == range) throw Bad("bad range coder start");
            }

            byte Next()
            {
                if (sp >= src.Length) throw Bad("input ends early");
                return src[sp++];
            }

            void Normalize()
            {
                if (range < TopValue) { range <<= 8; code = (code << 8) | Next(); }
            }

            int Bit(ushort[] probs, int i)
            {
                uint p = probs[i];
                uint bound = (range >> 11) * p;
                int bit;
                if (code < bound) { range = bound; probs[i] = (ushort)(p + ((2048 - p) >> 5)); bit = 0; }
                else { range -= bound; code -= bound; probs[i] = (ushort)(p - (p >> 5)); bit = 1; }
                Normalize();
                return bit;
            }

            uint DirectBits(int n)
            {
                uint res = 0;
                for (; n > 0; n--)
                {
                    range >>= 1;
                    code -= range;
                    uint t = 0u - (code >> 31);
                    code += range & t;
                    if (code == range) throw Bad("corrupted direct bits");
                    Normalize();
                    res = (res << 1) + (t + 1);
                }
                return res;
            }

            int Tree(ushort[] probs, int offset, int bits)
            {
                int m = 1;
                for (int i = 0; i < bits; i++) m = (m << 1) + Bit(probs, offset + m);
                return m - (1 << bits);
            }

            int ReverseTree(ushort[] probs, int offset, int bits)
            {
                int m = 1, sym = 0;
                for (int i = 0; i < bits; i++)
                {
                    int b = Bit(probs, offset + m);
                    m = (m << 1) + b;
                    sym |= b << i;
                }
                return sym;
            }

            sealed class LenDecoder
            {
                public readonly ushort[] Choice = Probs(2), Low = Probs(16 << 3), Mid = Probs(16 << 3), High = Probs(256);
            }

            int Len(LenDecoder l, int posState)
            {
                if (Bit(l.Choice, 0) == 0) return Tree(l.Low, posState << 3, 3);
                if (Bit(l.Choice, 1) == 0) return 8 + Tree(l.Mid, posState << 3, 3);
                return 16 + Tree(l.High, 0, 8);
            }

            uint Distance(int len)
            {
                int lenState = Math.Min(len, LenToPosStates - 1);
                int slot = Tree(posSlot, lenState << 6, 6);
                if (slot < 4) return (uint)slot;
                int direct = (slot >> 1) - 1;
                uint dist = (uint)((2 | (slot & 1)) << direct);
                if (slot < EndPosModelIndex) return dist + (uint)ReverseTree(posDecoders, (int)dist - slot, direct);
                dist += DirectBits(direct - AlignBits) << AlignBits;
                return dist + (uint)ReverseTree(align, 0, AlignBits);
            }

            public byte[] Run()
            {
                int state = 0;
                uint rep0 = 0, rep1 = 0, rep2 = 0, rep3 = 0;
                int pbMask = (1 << pb) - 1, lpMask = (1 << lp) - 1;
                while (op < outBuf.Length)
                {
                    int posState = op & pbMask;
                    if (Bit(isMatch, (state << PosBitsMax) + posState) == 0)
                    {
                        int prev = op > 0 ? outBuf[op - 1] : 0;
                        int lit = 0x300 * (((op & lpMask) << lc) + (prev >> (8 - lc)));
                        int sym = 1;
                        if (state >= 7)
                        {
                            if (rep0 >= op) throw Bad("match byte before the start");
                            int match = outBuf[op - (int)rep0 - 1];
                            while (sym < 0x100)
                            {
                                int mb = (match >> 7) & 1;
                                match <<= 1;
                                int b = Bit(literals, lit + ((1 + mb) << 8) + sym);
                                sym = (sym << 1) | b;
                                if (mb != b) break;
                            }
                        }
                        while (sym < 0x100) sym = (sym << 1) | Bit(literals, lit + sym);
                        outBuf[op++] = (byte)sym;
                        state = state < 4 ? 0 : state < 10 ? state - 3 : state - 6;
                        continue;
                    }
                    int len;
                    if (Bit(isRep, state) != 0)
                    {
                        if (op == 0) throw Bad("a repeated match at the start");
                        if (Bit(isRepG0, state) == 0)
                        {
                            if (Bit(isRep0Long, (state << PosBitsMax) + posState) == 0)
                            {
                                if (rep0 >= op) throw Bad("a short repeat before the start");
                                state = state < 7 ? 9 : 11;
                                outBuf[op] = outBuf[op - (int)rep0 - 1];
                                op++;
                                continue;
                            }
                        }
                        else
                        {
                            uint dist;
                            if (Bit(isRepG1, state) == 0) dist = rep1;
                            else
                            {
                                if (Bit(isRepG2, state) == 0) dist = rep2;
                                else { dist = rep3; rep3 = rep2; }
                                rep2 = rep1;
                            }
                            rep1 = rep0;
                            rep0 = dist;
                        }
                        if (rep0 >= op) throw Bad("a repeated match before the start");
                        len = Len(repLenDec, posState);
                        state = state < 7 ? 8 : 11;
                    }
                    else
                    {
                        rep3 = rep2; rep2 = rep1; rep1 = rep0;
                        len = Len(lenDec, posState);
                        state = state < 7 ? 7 : 10;
                        rep0 = Distance(len);
                        if (rep0 == 0xFFFFFFFF) break;   // the end marker
                        if (rep0 >= op) throw Bad("a match before the start");
                    }
                    len += 2;
                    if (op + len > outBuf.Length) len = outBuf.Length - op;
                    int from = op - (int)rep0 - 1;
                    for (int i = 0; i < len; i++) outBuf[op++] = outBuf[from + i];
                }
                if (op != outBuf.Length) throw Bad("decoded " + op + " of " + outBuf.Length + " bytes");
                return outBuf;
            }
        }
    }
}
