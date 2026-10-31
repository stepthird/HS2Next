// HS2's clothes-state rules (clean room, behaviour only) as a Unity-free
// value: the state type of each clothes kind after the pair reconciliation (top / bottom, bra / shorts), the one-piece
// flags, and SetClothesState's cycling and couplings. StudioOps.SetClothes computes the coupled values with it (U26: the
// stored document holds the coupled values); the realization shows an unsupported stored value as the state the cycling
// reaches from it (finding chara.clothesStateAdjusted) without writing it back.
// Kinds: 0 top, 1 bottom, 2 bra (inner top), 3 shorts (inner bottom), 4 gloves, 5 pantyhose, 6 socks, 7 shoes.
// States: 0 on, 1 half, 2 off. Types: 0 = three states, 1 = two states (on / off), -1 = no states (not a state kind).
using System;

namespace HS2Next.Scenes.Runtime
{
    public sealed class ClothesStateSet
    {
        public const int KindCount = 8;
        public const int Top = 0, Bottom = 1, Bra = 2, Shorts = 3, Gloves = 4, Pantyhose = 5, Socks = 6, Shoes = 7;
        public const int On = 0, Half = 1, Off = 2;
        public const int ThreeStates = 0, TwoStates = 1, NoStates = -1;

        /// <summary>The kind names of the bridge (`chara.clothes` states keys) in kind order.</summary>
        public static readonly string[] KindNames = { "top", "bottom", "bra", "shorts", "gloves", "pantyhose", "socks", "shoes" };

        static readonly int[] Three = { On, Half, Off }, Two = { On, Off }, NoneArr = new int[0];

        /// <summary>State type per kind after the reconciliation (see the type constants).</summary>
        public readonly int[] Types = { NoStates, NoStates, NoStates, NoStates, NoStates, NoStates, NoStates, NoStates };
        /// <summary>One-piece top (the top's list column `Coordinate` = 1): the bottom is not worn and couples with the top.</summary>
        public bool NotBot;
        /// <summary>The top forbids a bra (its column `NotBra` = 1): the bra object is hidden and has no states.</summary>
        public bool NotInnerT;
        /// <summary>One-piece inner (the bra's column `Coordinate` = 1): the shorts are not worn and couple with the bra.</summary>
        public bool NotInnerB;
        /// <summary>False for <see cref="Unknown"/> (the character is not built: every value is stored as given).</summary>
        public bool Known { get; private set; } = true;

        /// <summary>The set of a character whose outfit is not known (not built): no state kinds, values stored as given.</summary>
        public static ClothesStateSet Unknown => new ClothesStateSet { Known = false };

        public bool IsStateKind(int kind) => kind >= 0 && kind < KindCount && Types[kind] != NoStates;

        public bool Supports(int kind, int state)
        {
            if (!IsStateKind(kind)) return false;
            return Types[kind] == ThreeStates ? state >= 0 && state <= 2 : state == On || state == Off;
        }

        /// <summary>The supported states of a kind ([0, 1, 2], [0, 2] or []).</summary>
        public int[] Supported(int kind) => !IsStateKind(kind) ? NoneArr : Types[kind] == ThreeStates ? (int[])Three.Clone() : (int[])Two.Clone();

        /// <summary>The kind of a bridge name ("top", …) or a decimal index; -1 when neither.</summary>
        public static int KindOf(string nameOrIndex)
        {
            if (string.IsNullOrEmpty(nameOrIndex)) return -1;
            for (int k = 0; k < KindCount; k++) if (string.Equals(KindNames[k], nameOrIndex, StringComparison.OrdinalIgnoreCase)) return k;
            return int.TryParse(nameOrIndex, out int i) && i >= 0 && i < KindCount ? i : -1;
        }

        // ------------------------------------------------------------ the state types (pair reconciliation)

        /// <summary>
        /// The set from the list-row state types of the worn parts (<paramref name="rowTypes"/>[k] = the `StateType` column, or
        /// <see cref="int.MinValue"/> when no row was loaded for the kind) and whether the parts that define the coupled halves
        /// exist (<paramref name="botDefOfTop"/> = the top part has its bottom-half object `objBotDef`, and so on), as HS2
        /// reconciles them when parts load.
        /// </summary>
        public static ClothesStateSet FromRows(int[] rowTypes, bool notBot, bool notInnerT, bool notInnerB,
            bool botDefOfTop, bool topDefOfBot, bool botDefOfBra, bool botDefOfShorts)
        {
            var s = new ClothesStateSet { NotBot = notBot, NotInnerT = notInnerT, NotInnerB = notInnerB };
            int Row(int k) => rowTypes != null && k < rowTypes.Length && rowTypes[k] != int.MinValue ? rowTypes[k] : 3;
            // gloves, pantyhose, socks, shoes: their own type
            for (int k = Gloves; k <= Shoes; k++) s.Types[k] = TypeOf(Row(k));
            // top / bottom
            int t = Row(Top), b = Row(Bottom);
            if (b != 0 && botDefOfTop)
            {
                if (t == 0) b = 0;
                else if (t == 1 && b == 2) b = 1;
            }
            if (t != 0 && topDefOfBot)
            {
                if (b == 0) t = 0;
                else if (b == 1 && t == 2) t = 1;
            }
            s.Types[Top] = TypeOf(t);
            s.Types[Bottom] = TypeOf(b);
            // bra / shorts
            int br = notInnerT ? 3 : Row(Bra);
            int sh = !notInnerT || !notInnerB ? Row(Shorts) : 3;
            if (sh != 0 && botDefOfBra)
            {
                if (br == 0) sh = 0;
                else if (br == 1 && sh == 2) sh = 1;
            }
            if (br != 0 && br != 2 && botDefOfShorts)
            {
                if (sh == 0) br = 0;
                else if (sh == 1 && br == 2) br = 1;
            }
            s.Types[Bra] = TypeOf(br);
            s.Types[Shorts] = TypeOf(sh);
            return s;
        }

        static int TypeOf(int rowType) => rowType == 0 ? ThreeStates : rowType == 1 ? TwoStates : NoStates;

        /// <summary>A state type column as HS2 parses it; anything unreadable counts as "no states" (3).</summary>
        public static int ParseStateType(string column) => int.TryParse(column, out int v) && v >= 0 && v <= 255 ? v : 3;

        // ------------------------------------------------------------ SetClothesState (cycling and couplings)

        /// <summary>
        /// HS2's SetClothesState on a copy of <paramref name="current"/> (8 entries, 0..2): a kind without states stores the
        /// value as given; otherwise the requested state is taken when supported, else the next state in the cycling direction
        /// (+1 with <paramref name="next"/>, −1 otherwise, mod 3) until one is supported or the cycle reaches the current state
        /// again (then the value stays). Couplings: with a one-piece top, top off forces the bottom off and a top leaving off
        /// while the bottom is off gives the bottom the (cycled) requested state; symmetrically from the bottom; the same for
        /// bra / shorts with a one-piece inner.
        /// </summary>
        public int[] Apply(int[] current, int kind, int state, bool next = true)
        {
            var s = Copy(current);
            if (kind < 0 || kind >= KindCount) return s;
            int start = s[kind];
            do
            {
                if (!IsStateKind(kind)) { s[kind] = state; break; }
                if (Supports(kind, state)) { s[kind] = state; break; }
                state = next ? (state + 1) % 3 : (state + 2) % 3;
            }
            while (start != state);
            void Couple(bool flag, int a, int b)
            {
                if (!flag) return;
                if (s[a] == Off) s[b] = Off;
                else if (s[b] == Off) s[b] = state;
            }
            switch (kind)
            {
                case Top: Couple(NotBot, Top, Bottom); break;
                case Bottom: Couple(NotBot, Bottom, Top); break;
                case Bra: Couple(NotInnerB, Bra, Shorts); break;
                case Shorts: Couple(NotInnerB, Shorts, Bra); break;
            }
            return s;
        }

        /// <summary>"All" buttons: <see cref="Apply"/> for the eight kinds in order.</summary>
        public int[] ApplyAll(int[] current, int state)
        {
            var s = Copy(current);
            for (int k = 0; k < KindCount; k++) s = Apply(s, k, state);
            return s;
        }

        /// <summary>
        /// The state a stored value is realized as: the value itself when the kind supports it or has no states, else the first
        /// supported state the forward cycling reaches (kept as stored, realized as the nearest supported state).
        /// </summary>
        public int Effective(int kind, int stored)
        {
            if (!IsStateKind(kind) || Supports(kind, stored)) return stored;
            for (int i = 1; i <= 2; i++)
            {
                int v = ((stored % 3) + 3 + i) % 3;
                if (Supports(kind, v)) return v;
            }
            return stored;
        }

        /// <summary>Every kind's <see cref="Effective"/> value.</summary>
        public int[] EffectiveAll(int[] stored)
        {
            var s = Copy(stored);
            for (int k = 0; k < KindCount; k++) s[k] = Effective(k, s[k]);
            return s;
        }

        static int[] Copy(int[] a)
        {
            var s = new int[KindCount];
            if (a != null) Array.Copy(a, s, Math.Min(a.Length, KindCount));
            return s;
        }

        public override string ToString()
        {
            var parts = new string[KindCount];
            for (int k = 0; k < KindCount; k++) parts[k] = KindNames[k] + ":" + (Types[k] == ThreeStates ? "3" : Types[k] == TwoStates ? "2" : "-");
            return (Known ? "" : "unknown ") + string.Join(" ", parts) + (NotBot ? " notBot" : "") + (NotInnerT ? " notInnerT" : "") + (NotInnerB ? " notInnerB" : "");
        }
    }
}
