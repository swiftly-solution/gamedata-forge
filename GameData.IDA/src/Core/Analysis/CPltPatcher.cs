using System.Text;
using GameData.IDA.Core.Native;

namespace GameData.IDA.Core.Analysis;

internal readonly record struct PltPatchResult(bool Applicable, int Patched, int Created, int Unresolved)
{
    internal static PltPatchResult NotApplicable => new(false, 0, 0, 0);
}

internal static unsafe class CPltPatcher
{
    private const ulong EhdrPhoff = 0x20;
    private const ulong EhdrPhentsize = 0x36;
    private const ulong EhdrPhnum = 0x38;

    private const ulong PhdrType = 0x00;
    private const ulong PhdrVaddr = 0x10;
    private const ulong PhdrMemsz = 0x28;

    private const ulong RelaOffset = 0x00;
    private const ulong RelaType = 0x08;
    private const ulong RelaSymbol = 0x0C;
    private const ulong RelaSize = 24;

    private const ulong SymbolSize = 0x18;

    private const uint PtDynamic = 2;

    private const ulong DtPltRelSz = 2;
    private const ulong DtStrTab = 5;
    private const ulong DtSymTab = 6;
    private const ulong DtJmpRel = 0x17;

    private const uint RelocationJumpSlot = 7;

    private const ulong DynamicEntrySize = 16;

    private const int MaxSymbolLength = 4096;

    internal static PltPatchResult Run()
    {
        ulong imageBase = Ida.getinf(IdaAbi.Current.MinEa);

        if (!IsElf64(imageBase))
        {
            return PltPatchResult.NotApplicable;
        }

        if (!TryFindDynamic(imageBase, out ulong dynamic, out ulong dynamicSize))
        {
            return PltPatchResult.NotApplicable;
        }

        ulong jmprel = FindDynamicEntry(dynamic, dynamicSize, DtJmpRel);
        ulong strtab = FindDynamicEntry(dynamic, dynamicSize, DtStrTab);
        ulong symtab = FindDynamicEntry(dynamic, dynamicSize, DtSymTab);

        if (jmprel == 0 || strtab == 0 || symtab == 0)
        {
            return PltPatchResult.NotApplicable;
        }

        ulong relsz = FindDynamicEntry(dynamic, dynamicSize, DtPltRelSz);
        if (relsz == 0)
        {
            segment_t* segment = Ida.getseg(jmprel);
            if (segment == null)
            {
                return PltPatchResult.NotApplicable;
            }

            relsz = ((range_t*)segment)->EndEa - jmprel;
        }

        var externs = new ExternSegment();
        int patched = 0;
        int unresolved = 0;

        for (ulong offset = 0; offset + RelaSize <= relsz; offset += RelaSize)
        {
            ulong entry = jmprel + offset;

            if (Ida.get_dword(entry + RelaType) != RelocationJumpSlot)
            {
                continue;
            }

            ulong slot = Ida.get_qword(entry + RelaOffset);
            uint symbolIndex = Ida.get_dword(entry + RelaSymbol);

            uint nameOffset = Ida.get_dword(symtab + (symbolIndex * SymbolSize));
            string? name = ReadCString(strtab + nameOffset);

            if (string.IsNullOrEmpty(name) || !IsMapped(slot))
            {
                unresolved++;
                continue;
            }

            ulong target = Resolve(name, externs);

            SetName(slot, name + "_ptr");

            if (target == IdaConstants.BadAddress)
            {
                unresolved++;
                continue;
            }

            Ida.put_qword(slot, target);
            Ida.add_dref(slot, target, IdaConstants.Dref.Offset);

            RedirectCallers(slot, target, name);
            patched++;
        }

        Ida.auto_wait();

        return new PltPatchResult(true, patched, externs.Created, unresolved);
    }

    private static void RedirectCallers(ulong slot, ulong target, string name)
    {
        for (ulong reference = Ida.get_first_dref_to(slot);
             reference != IdaConstants.BadAddress;
             reference = Ida.get_next_dref_to(slot, reference))
        {
            Ida.add_cref(reference, target, IdaConstants.Cref.CallNear);

            func_t* function = Ida.get_func(reference);
            if (function == null)
            {
                continue;
            }

            ulong start = ((range_t*)function)->StartEa;

            SetName(start, "_" + name);
            FuncRecordAccess.AddFlags(function, IdaConstants.FuncFlags.Thunk);
        }
    }

    private static ulong Resolve(string name, ExternSegment externs)
    {
        byte* native = Utf8.Allocate(name);

        try
        {
            ulong found = Ida.get_name_ea(IdaConstants.BadAddress, native);
            if (found != IdaConstants.BadAddress)
            {
                return found;
            }
        }
        finally
        {
            Utf8.Free(native);
        }

        return externs.Find(name) ?? externs.Create(name) ?? IdaConstants.BadAddress;
    }

    private static bool IsElf64(ulong imageBase)
        => IsMapped(imageBase)
        && Ida.get_byte(imageBase + 0) == 0x7F
        && Ida.get_byte(imageBase + 1) == (byte)'E'
        && Ida.get_byte(imageBase + 2) == (byte)'L'
        && Ida.get_byte(imageBase + 3) == (byte)'F'
        && Ida.get_byte(imageBase + 4) == 2;

    private static bool TryFindDynamic(ulong imageBase, out ulong address, out ulong size)
    {
        address = 0;
        size = 0;

        ulong headers = Ida.get_qword(imageBase + EhdrPhoff) + imageBase;
        ushort entrySize = Ida.get_word(imageBase + EhdrPhentsize);
        ushort count = Ida.get_word(imageBase + EhdrPhnum);

        if (entrySize == 0 || count == 0 || !IsMapped(headers))
        {
            return false;
        }

        for (ushort i = 0; i < count; i++)
        {
            ulong header = headers + (entrySize * (ulong)i);

            if (Ida.get_dword(header + PhdrType) != PtDynamic)
            {
                continue;
            }

            address = Ida.get_qword(header + PhdrVaddr);

            size = Ida.get_qword(header + PhdrMemsz);

            return IsMapped(address);
        }

        return false;
    }

    private static ulong FindDynamicEntry(ulong dynamic, ulong size, ulong tag)
    {
        for (ulong offset = 0; offset + DynamicEntrySize <= size; offset += DynamicEntrySize)
        {
            ulong current = Ida.get_qword(dynamic + offset);
            ulong value = Ida.get_qword(dynamic + offset + 8);

            if (current == 0 && value == 0)
            {
                break;
            }

            if (current == tag)
            {
                return value;
            }
        }

        return 0;
    }

    private static string? ReadCString(ulong address)
    {
        if (!IsMapped(address))
        {
            return null;
        }

        var builder = new StringBuilder();

        for (int i = 0; i < MaxSymbolLength; i++)
        {
            byte value = Ida.get_byte(address + (ulong)i);
            if (value == 0)
            {
                break;
            }

            builder.Append((char)value);
        }

        return builder.Length > 0 ? builder.ToString() : null;
    }

    private static bool IsMapped(ulong address) => Ida.is_mapped(address) != 0;

    private static void SetName(ulong address, string name)
    {
        byte* native = Utf8.Allocate(name);

        try
        {
            Ida.set_name(address, native, IdaConstants.SetName.Force | IdaConstants.SetName.NoWarn);
        }
        finally
        {
            Utf8.Free(native);
        }
    }

    private sealed class ExternSegment
    {
        private Dictionary<string, ulong>? _entries;

        internal int Created { get; private set; }

        internal ulong? Find(string name)
        {
            Index();
            return _entries!.TryGetValue(name, out ulong ea) ? ea : null;
        }

        internal ulong? Create(string name)
        {
            segment_t* segment = Segment();
            if (segment == null)
            {
                return null;
            }

            var range = (range_t*)segment;
            ulong target = range->EndEa;

            if (Ida.set_segm_end(range->StartEa, target + 8, IdaConstants.SegMod.Keep) == 0)
            {
                return null;
            }

            Ida.put_qword(target, 0);

            var record = FuncRecord.Create(target, target + 8);
            if (Ida.add_func_ex(FuncRecordAccess.AsPointer(ref record)) == 0)
            {
                return null;
            }

            SetName(target, name);

            Index();
            _entries![name] = target;
            Created++;

            return target;
        }

        private static segment_t* Segment()
        {
            byte* native = Utf8.Allocate("extern");

            try
            {
                return Ida.get_segm_by_name(native);
            }
            finally
            {
                Utf8.Free(native);
            }
        }

        private void Index()
        {
            if (_entries != null)
            {
                return;
            }

            _entries = new Dictionary<string, ulong>(StringComparer.Ordinal);

            segment_t* segment = Segment();
            if (segment == null)
            {
                return;
            }

            var range = (range_t*)segment;

            for (ulong ea = range->StartEa; ea < range->EndEa && ea != IdaConstants.BadAddress;
                 ea = Ida.next_head(ea, range->EndEa))
            {
                using var buffer = new QStringBuffer();

                if (Ida.get_func_name(buffer.Pointer, ea) > 0)
                {
                    _entries.TryAdd(buffer.ToString(), ea);
                }
            }
        }
    }
}
