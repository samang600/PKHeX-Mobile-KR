using System.Buffers.Binary;
using System.Text;
using PKHeX.Core;

namespace PKHeXKR;

/// <summary>
/// 이상한 소포 앨범(받은 선물 기록) 보기·편집 — 소드·실드 / BDSP / 레전드 아르세우스.
/// 기록 구조는 theSLAYER의 Wonder Records Tool(PKHeX 플러그인)을 참고해 새로 작성.
/// 앨범 기록은 화면 표시용이라 포켓몬이 생기거나 선물을 다시 받을 수 있게 되지는 않음.
/// (레츠고 앨범은 '이상한 소포 받기함'에서 다룸)
/// </summary>
public sealed class WonderRecordsSheet : Sheet
{
    private readonly Album album;
    private readonly VerticalStackLayout list = new() { Spacing = 4 };
    private readonly Label status = T.L("", 12, sub: true);

    public static bool Supported(SaveFile sav) => Album.For(sav) != null;

    public WonderRecordsSheet() : base("이상한 소포 앨범")
    {
        album = Album.For(AppState.Sav);
        var note = T.L("게임의 '이상한 소포 앨범'에 보이는 받은 선물 기록을 편집합니다. 기록은 표시용이라 포켓몬이 생기거나 선물을 다시 받을 수 있게 되지는 않습니다. 바꾼 뒤에는 세이브를 내보내야 적용됩니다.", 12, sub: true);
        note.LineBreakMode = LineBreakMode.WordWrap;
        var buttons = new HorizontalStackLayout { Spacing = 8 };
        if (album != null)
        {
            var import = T.Pill(album.CanConvertWC8 ? $"{album.Ext} / WC8 넣기" : $"{album.Ext} 넣기", primary: true);
            import.Clicked += async (_, _) => await Import();
            buttons.Add(import);
        }
        Body.Add(new ScrollView { Content = new VerticalStackLayout { Spacing = 10, Children = { note, buttons, status, list } } });
        Fill();
    }

    private void Fill()
    {
        list.Children.Clear();
        if (album == null || !album.Ready)
        {
            status.Text = "이 세이브에서 이상한 소포 앨범을 찾지 못했습니다.";
            return;
        }
        int used = 0;
        for (int i = 0; i < album.Count; i++)
        {
            bool empty = album.IsEmpty(i);
            if (!empty) used++;
            string title = empty ? "(비어 있음)" : $"{album.CardID(i):0000} · {album.TypeName(i)} · {album.Describe(i)}";
            var date = !empty && album.GetDate(i) is { } d ? $"  {d:yyyy-MM-dd HH:mm}" : "";
            var label = T.L($"{i + 1:00}. {title}{date}", 13, bold: !empty);
            label.LineBreakMode = LineBreakMode.WordWrap;
            var border = T.Card(label, 9);
            var tap = new TapGestureRecognizer();
            int slot = i;
            tap.Tapped += async (_, _) => await OnRecord(slot);
            border.GestureRecognizers.Add(tap);
            list.Children.Add(border);
        }
        status.Text = $"{album.Name} · {used}/{album.Count}개 기록 · 칸을 누르면 편집";
    }

    private static Page Host => Application.Current.Windows[0].Page;

    private void Changed(string msg)
    {
        AppState.Sav.State.Edited = true;
        Fill();
        Note.Show(msg + " · 세이브 내보내기로 저장하세요");
    }

    private async Task OnRecord(int index)
    {
        bool empty = album.IsEmpty(index);
        const string edit = "받은 날짜·시각 바꾸기", export = "기록 파일로 내보내기", put = "이 칸에 넣기", clear = "기록 비우기";
        var opts = empty ? new[] { put } : new[] { edit, export, put };
        var choice = await Host.DisplayActionSheetTr(empty ? $"{index + 1}번 칸 (비어 있음)" : $"{index + 1}번 칸 · {album.CardID(index):0000} {album.Describe(index)}", "취소", empty ? null : clear, opts);
        if (choice == edit)
        {
            var cur = album.GetDate(index) ?? DateTime.Now;
            SheetHost.Show(new RecordDateSheet(index + 1, cur, picked => { album.SetDate(index, picked); Changed("받은 날짜를 바꿨습니다"); }));
        }
        else if (choice == export)
        {
            await using var ms = new MemoryStream(album.Slot(index).ToArray());
            var result = await CommunityToolkit.Maui.Storage.FileSaver.Default.SaveAsync($"{album.CardID(index):0000}{album.Ext}", ms, CancellationToken.None);
            Note.Show(ShareUtil.SaveNote(result, "기록 파일을 저장했습니다", "저장을 취소했습니다"));
        }
        else if (choice == put) await Import(index);
        else if (choice == clear)
        {
            album.Slot(index).Clear();
            Changed("기록을 비웠습니다");
        }
    }

    private async Task Import(int target = -1)
    {
        try
        {
            var file = await FilePicker.PickAsync();
            if (file == null) return;
            byte[] bytes;
            await using (var input = await file.OpenReadAsync())
            using (var ms = new MemoryStream()) { await input.CopyToAsync(ms); bytes = ms.ToArray(); }

            byte[] rec;
            string what;
            if (bytes.Length == album.Size)
            {
                rec = bytes; what = "기록";
            }
            else if (album.CanConvertWC8 && bytes.Length == WC8.Size)
            {
                rec = SwshAlbum.FromWC8(bytes, AppState.Sav); what = "WC8 카드를 기록으로 바꿔";
            }
            else
            {
                Note.Show(album.CanConvertWC8 ? $"{album.Ext} 기록({album.Size}바이트) 또는 WC8 카드({WC8.Size}바이트)를 고르세요" : $"{album.Ext} 기록 파일({album.Size}바이트)을 고르세요");
                return;
            }

            if (target < 0) target = Enumerable.Range(0, album.Count).FirstOrDefault(album.IsEmpty, -1);
            if (target < 0)
            {
                var choices = Enumerable.Range(0, album.Count).Select(i => $"{i + 1:00}번 칸 덮어쓰기 · {album.Describe(i)}").ToArray();
                var sel = await Host.DisplayActionSheetTr("앨범이 가득 찼습니다", "취소", null, choices);
                target = Array.IndexOf(choices, sel);
                if (target < 0) return;
            }
            rec.AsSpan().CopyTo(album.Slot(target));
            Changed($"{what} {target + 1}번 칸에 넣었습니다");
        }
        catch (Exception ex) { status.Text = "넣지 못했습니다: " + ex.Message; }
    }

    // ───────────────────────── 게임별 앨범 ─────────────────────────

    private abstract class Album
    {
        public abstract string Name { get; }
        public abstract int Count { get; }
        public abstract int Size { get; }
        public abstract string Ext { get; }
        public virtual bool CanConvertWC8 => false;
        public abstract bool Ready { get; }
        public abstract Span<byte> Slot(int i);
        protected abstract int TypeOffset { get; }
        protected abstract string[] Types { get; }
        public abstract DateTime? GetDate(int i);
        public abstract void SetDate(int i, DateTime d);
        public abstract string Describe(int i);

        public ushort CardID(int i) => BinaryPrimitives.ReadUInt16LittleEndian(Slot(i)[8..]);
        public bool IsEmpty(int i) { var s = Slot(i); return BinaryPrimitives.ReadUInt64LittleEndian(s) == 0 && CardID(i) == 0 && s[TypeOffset] == 0; }
        public string TypeName(int i) { int t = Slot(i)[TypeOffset]; return t < Types.Length ? Types[t] : $"종류 {t}"; }
        protected int Type(int i) => Slot(i)[TypeOffset];

        protected static string Sp(ushort sp, int form)
        {
            var s = GameInfo.Strings;
            var n = sp < s.specieslist.Length ? s.specieslist[sp] : $"#{sp}";
            return form > 0 ? $"{n}-{form}" : n;
        }
        protected static string Item(ushort id)
        {
            var items = GameInfo.Strings.GetItemStrings(AppState.Sav.Context, AppState.Sav.Version);
            return id < items.Length ? items[id] : $"도구 #{id}";
        }

        // 8바이트 시각: 작으면 유닉스 초, 크면 윈도우 FileTime (BDSP·아르세우스 — 원본 툴과 같은 판정)
        protected static DateTime? ReadTicks(long v)
        {
            if (v <= 0) return null;
            try { return v < 95649080399L ? DateTimeOffset.FromUnixTimeSeconds(v).LocalDateTime : DateTime.FromFileTimeUtc(v).ToLocalTime(); }
            catch { return null; }
        }
        protected static long WriteTicks(long old, DateTime d) =>
            old > 0 && old < 95649080399L ? new DateTimeOffset(d).ToUnixTimeSeconds() : d.ToUniversalTime().ToFileTimeUtc();

        public static Album For(SaveFile sav) => sav switch
        {
            SAV8SWSH s => new SwshAlbum(s),
            SAV8BS b => new BdspAlbum(b),
            SAV8LA a => new PlaAlbum(a),
            _ => null,
        };
    }

    private sealed class SwshAlbum(SAV8SWSH sav) : Album
    {
        private const uint Key = 0x112D5141;
        public override string Name => "소드·실드";
        public override int Count => 50;
        public override int Size => 0x68;
        public override string Ext => ".wr8";
        public override bool CanConvertWC8 => true;
        protected override int TypeOffset => 0x0C;
        protected override string[] Types => ["없음", "포켓몬", "도구", "BP", "의상", "머니"];
        private SCBlock Block => sav.Accessor.TryGetBlock(Key, out var b) ? b : null;
        public override bool Ready => Block is { } b && b.Data.Length >= Count * Size;
        public override Span<byte> Slot(int i) => Block.Data.Slice(i * Size, Size);

        // 연 12비트 <<26 · 월 <<22 · 일 <<17 · 시 <<12 · 분 <<6 · 초
        public override DateTime? GetDate(int i)
        {
            ulong v = BinaryPrimitives.ReadUInt64LittleEndian(Slot(i));
            if (v == 0) return null;
            try { return new DateTime((int)(v >> 26 & 0xFFF), (int)(v >> 22 & 0xF), (int)(v >> 17 & 0x1F), (int)(v >> 12 & 0x1F), (int)(v >> 6 & 0x3F), (int)(v & 0x3F)); }
            catch { return null; }
        }
        public override void SetDate(int i, DateTime d) => BinaryPrimitives.WriteUInt64LittleEndian(Slot(i), Pack(d));
        private static ulong Pack(DateTime d) =>
            ((ulong)(d.Year & 0xFFF) << 26) | ((ulong)d.Month << 22) | ((ulong)d.Day << 17) | ((ulong)d.Hour << 12) | ((ulong)d.Minute << 6) | (ulong)d.Second;

        public override string Describe(int i)
        {
            byte[] s = Slot(i).ToArray();
            ushort U(int o) => BinaryPrimitives.ReadUInt16LittleEndian(s.AsSpan(o));
            return Type(i) switch
            {
                1 => (s[0x12] == 1 ? "알 " : "") + Sp(U(0x30), U(0x32)),
                2 => Item(U(0x30)) + (U(0x32) > 1 ? $" ×{U(0x32)}" : "") + (s[0x0D] > 1 ? $" 외 {s[0x0D] - 1}종" : ""),
                3 => $"{U(0x30)} BP",
                4 => $"의상 {s[0x0D]}벌",
                5 => $"{U(0x30)}원",
                _ => "",
            };
        }

        /// <summary>WC8(720바이트) → 앨범 기록(WR8, 104바이트). 원본 툴의 변환 규칙과 같음.</summary>
        public static byte[] FromWC8(byte[] src, SaveFile sav)
        {
            var dst = new byte[0x68];
            BinaryPrimitives.WriteUInt64LittleEndian(dst, Pack(DateTime.Now));
            dst[8] = src[8]; dst[9] = src[9];        // 카드 번호
            dst[0x0A] = src[0x15];                   // 카드 제목
            int type = src[0x11];
            dst[0x0C] = (byte)type;
            dst[0x0E] = 1;
            dst[0x0F] = src[0x1C];                   // 시즌
            int lang = sav.Language is >= 1 and <= 10 and not 6 ? sav.Language : 2;
            int[] otOfs = [0, 300, 328, 356, 384, 412, 0, 440, 468, 496, 524];
            int[] lgOfs = [0, 74, 102, 130, 158, 186, 0, 214, 242, 270, 298];
            switch (type)
            {
                case 1: // 포켓몬
                    Array.Copy(src, 0x22E, dst, 0x10, 2);            // 지닌 도구
                    dst[0x12] = src[0x245];                         // 알
                    Array.Copy(src, 0x240, dst, 0x30, 2);            // 종류
                    dst[0x32] = src[0x242];                         // 폼
                    for (int m = 0; m < 4; m++) Array.Copy(src, 0x230 + m * 2, dst, 0x38 + m * 4, 2);   // 기술 (4바이트 간격)
                    bool hasOT = src.AsSpan(otOfs[lang], 26).IndexOfAnyExcept((byte)0) >= 0;
                    if (hasOT) Array.Copy(src, otOfs[lang], dst, 0x48, 26);
                    else { var ot = Encoding.Unicode.GetBytes(sav.OT); Array.Copy(ot, 0, dst, 0x48, Math.Min(ot.Length, 24)); }
                    dst[0x34] = !hasOT || src[0x272] == 3 ? sav.Gender : src[0x272];   // 어버이 성별
                    dst[0x62] = src[lgOfs[lang]] == 0 ? (byte)sav.Language : src[lgOfs[lang]];
                    dst[0x63] = src[0x25C] == 0xFF ? (byte)98 : src[0x25C];       // 리본
                    dst[0x64] = src[0x243];                         // 성별
                    break;
                case 2: // 도구 6칸
                    int n = 0;
                    for (int k = 0; k < 6; k++)
                    {
                        Array.Copy(src, 0x20 + k * 4, dst, 0x30 + k * 4, 4);
                        if (BinaryPrimitives.ReadUInt16LittleEndian(src.AsSpan(0x20 + k * 4)) > 0) n++;
                    }
                    dst[0x0D] = (byte)n;
                    break;
                case 3: case 5: // BP · 머니
                    Array.Copy(src, 0x20, dst, 0x30, 2);
                    break;
                case 4: // 의상: 플레이어 성별에 맞는 목록
                    Array.Copy(src, sav.Gender == 1 ? 0x50 : 0x20, dst, 0x30, 48);
                    int c = 0;
                    for (int k = 0; k < 6; k++) if (BinaryPrimitives.ReadUInt32LittleEndian(dst.AsSpan(0x34 + k * 8)) != uint.MaxValue) c++;
                    dst[0x0D] = (byte)c;
                    break;
            }
            return dst;
        }
    }

    private sealed class BdspAlbum(SAV8BS sav) : Album
    {
        public override string Name => "브릴리언트 다이아몬드·샤이닝 펄";
        public override int Count => MysteryBlock8b.RecvDataMax;
        public override int Size => RecvData8b.SIZE;
        public override string Ext => ".wrb8";
        protected override int TypeOffset => 0x0C;
        protected override string[] Types => ["없음", "포켓몬", "도구", "BP", "의상", "머니", "조각상"];
        public override bool Ready => sav.MysteryRecords.Data.Length >= MysteryBlock8b.OFS_RECV + Count * Size;
        public override Span<byte> Slot(int i) => sav.MysteryRecords.Data.Slice(MysteryBlock8b.OFS_RECV + i * Size, Size);
        public override DateTime? GetDate(int i) => ReadTicks(BinaryPrimitives.ReadInt64LittleEndian(Slot(i)));
        public override void SetDate(int i, DateTime d) => BinaryPrimitives.WriteInt64LittleEndian(Slot(i), WriteTicks(BinaryPrimitives.ReadInt64LittleEndian(Slot(i)), d));
        public override string Describe(int i)
        {
            byte[] s = Slot(i).ToArray();
            ushort U(int o) => BinaryPrimitives.ReadUInt16LittleEndian(s.AsSpan(o));
            return Type(i) switch
            {
                1 => (s[0x39] == 1 ? "알 " : "") + Sp(U(0x10), U(0x12)),
                2 => Item(U(0x40)) + (U(0x42) > 1 ? $" ×{U(0x42)}" : ""),
                3 or 5 => $"{BinaryPrimitives.ReadUInt32LittleEndian(s.AsSpan(0xCC))}",
                _ => "",
            };
        }
    }

    private sealed class PlaAlbum(SAV8LA sav) : Album
    {
        private const uint Key = 0x99E1625E;
        public override string Name => "레전드 아르세우스";
        public override int Count => 50;
        public override int Size => 632;
        public override string Ext => ".wra8";
        protected override int TypeOffset => 0x0E;
        protected override string[] Types => ["없음", "포켓몬", "도구", "의상", "BP", "머니", "조각상"];
        private SCBlock Block => sav.Accessor.TryGetBlock(Key, out var b) ? b : null;
        public override bool Ready => Block is { } b && b.Data.Length >= Count * Size;
        public override Span<byte> Slot(int i) => Block.Data.Slice(i * Size, Size);
        public override DateTime? GetDate(int i) => ReadTicks(BinaryPrimitives.ReadInt64LittleEndian(Slot(i)));
        public override void SetDate(int i, DateTime d) => BinaryPrimitives.WriteInt64LittleEndian(Slot(i), WriteTicks(BinaryPrimitives.ReadInt64LittleEndian(Slot(i)), d));
        public override string Describe(int i)
        {
            byte[] s = Slot(i).ToArray();
            ushort U(int o) => BinaryPrimitives.ReadUInt16LittleEndian(s.AsSpan(o));
            return Type(i) switch
            {
                1 => Sp(U(0x230), s[0x232]),
                2 => Item(U(0x10)) + (U(0x12) > 1 ? $" ×{U(0x12)}" : ""),
                _ => "",
            };
        }
    }

    private sealed class RecordDateSheet : Sheet
    {
        public RecordDateSheet(int slot, DateTime initial, Action<DateTime> apply) : base($"{slot}번 칸 받은 날짜", 0.46)
        {
            var date = new DatePicker { Date = initial.Date, Format = "yyyy-MM-dd", MinimumDate = new DateTime(2000, 1, 1), MaximumDate = new DateTime(2060, 12, 31) };
            var time = new TimePicker { Time = initial.TimeOfDay, Format = "HH:mm" };
            var ok = T.Pill("적용", primary: true);
            var cancel = T.Pill("취소");
            ok.Clicked += (_, _) => { apply((date.Date ?? DateTime.Today).Date + (time.Time ?? TimeSpan.Zero)); Close(); };
            cancel.Clicked += (_, _) => Close();
            Body.Add(new VerticalStackLayout { Spacing = 8, Children = { T.L("앨범에 표시되는 받은 날짜·시각입니다.", 12, sub: true), date, time, new HorizontalStackLayout { Spacing = 8, Children = { ok, cancel } } } });
        }
    }
}
