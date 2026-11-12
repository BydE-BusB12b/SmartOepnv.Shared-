using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SmartOepnv.AppShared.Helpers;
using SmartOepnv.Core.RoutePackage;

namespace SmartOepnv.AppShared.Views;

/// <summary>
/// Zeichenfeld für ein bereits vorhandenes Mobitec-Ziel: Front 128×16, Seite 120×16, Linie 28×16.
/// </summary>
public sealed class MobitecGraphicToolWindow : Window
{
    private const int Cell = 7;

    private static readonly SolidColorBrush Amber = Freeze(Color.FromRgb(0xFF, 0xC1, 0x07));
    private static readonly SolidColorBrush SignBlack = Freeze(Color.FromRgb(0x08, 0x08, 0x08));
    private static readonly SolidColorBrush CellOff = Freeze(Color.FromRgb(0x18, 0x18, 0x18));
    private static readonly SolidColorBrush SelectionFill = Freeze(Color.FromArgb(0x66, 0x80, 0xD8, 0xFF));
    private static readonly Pen SelectionPen = CreateSelectionPen();

    private readonly IReadOnlyList<MobitecSignFont> _fonts;
    private readonly Board[] _boards;
    private readonly ComboBox _fontBox;
    private readonly TextBox _textBox;
    private readonly TextBox _nameBox;
    private readonly TextBox _numberBox;
    private readonly TextBox _lineBox;
    private readonly TextBlock _formMessage;
    private int? _ownDestinationNumber;
    private bool _createNew;
    private readonly HashSet<int> _usedDestinationNumbers;
    private readonly Button[] _toolButtons;
    private readonly StackPanel _pageButtons = new() { Orientation = Orientation.Horizontal };
    private readonly TextBlock _pageLabel;
    private readonly TextBox _intervalBox;
    private int _page;
    private Board _active;
    private Tool _tool = Tool.Point;
    private bool _dragging;
    private bool _eraseDrag;
    private int _lineCol;
    private int _lineRow;
    private int _markCol;
    private int _markRow;
    private List<(int X, int Y)>? _clipboard;
    private MobitecGraphicEdit? _result;
    private readonly Action<MobitecGraphicEdit>? _persist;
    private Button? _saveButton;
    private bool _dirty;
    private bool _allowClose;
    private Board? _selectionBoard;
    private int _selectionPage;
    private bool[,]? _selectionBase;
    private readonly List<(int X, int Y)> _selectionInk = [];
    private readonly List<PlacedMark> _marks = [];
    private PlacedMark? _activeMark;
    private bool _selectionMoveUndo;
    private int _selectionShiftX;
    private int _selectionShiftY;
    private bool _suppressFontResize;
    private bool _hasField;
    private int _fieldX;
    private int _fieldY;
    private int _fieldW;
    private int _fieldH;
    private int _clipboardW;
    private int _clipboardH;

    private MobitecGraphicToolWindow(
        OutsideDisplayProgram? program,
        IReadOnlyList<MobitecSignFont> fonts,
        IEnumerable<int> usedDestinationNumbers,
        Action<MobitecGraphicEdit>? persist)
    {
        _fonts = fonts
            .OrderBy(font => font.IsFavorite ? 0 : 1)
            .ThenBy(font => font.IsFavorite ? Array.IndexOf(MobitecSignFont.FavoriteIds, font.Id) : 0)
            .ToList();
        _persist = persist;
        _createNew = program is null;
        _ownDestinationNumber = program?.DestinationNumber;
        _usedDestinationNumbers = new HashSet<int>(usedDestinationNumbers);
        Title = "Mobitec Grafiktool";
        Width = 1180;
        Height = 760;
        MinWidth = 760;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = UiTheme.BackgroundBrush;
        Foreground = LabelBrush();

        _boards =
        [
            new Board("Front", "128×16", MobitecBitmapFrame.FrontAddress, MobitecBitmapFrame.FrontWidth, program?.MobitecFrontFrame, fonts),
            new Board("Seite", "120×16", MobitecBitmapFrame.SideAddress, MobitecBitmapFrame.SideWidth, program?.MobitecSideFrame, fonts),
            new Board("Linie", "28×16", MobitecBitmapFrame.LineAddress, MobitecBitmapFrame.LineWidth, program?.MobitecLineFrame, fonts)
        ];
        foreach (var board in _boards)
        {
            board.Changed = MarkDirty;
        }

        _active = _boards[0];

        var root = new DockPanel { Margin = new Thickness(16) };

        var form = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        form.Children.Add(Label("Name"));
        _nameBox = FieldBox(program?.Name ?? string.Empty, 220);
        form.Children.Add(_nameBox);
        form.Children.Add(Label("Zielnr."));
        var numberText = program?.DestinationNumber?.ToString()
            ?? NextFreeDestinationNumber()?.ToString()
            ?? string.Empty;
        _numberBox = FieldBox(numberText, 70);
        form.Children.Add(_numberBox);

        var sequenced = _boards.FirstOrDefault(board => board.PageCount > 1);
        var interval = sequenced?.IntervalSeconds ?? Math.Clamp(program?.IntervalSeconds ?? 3, 1, 99);
        form.Children.Add(Label("Wechseltext"));
        form.Children.Add(_pageButtons);
        form.Children.Add(MakeButton("Seite dazu", (_, _) => AddPage()));
        form.Children.Add(MakeButton("Seite weg", (_, _) => RemovePage()));
        form.Children.Add(Label("Takt"));
        _intervalBox = FieldBox(interval.ToString(), 52);
        form.Children.Add(_intervalBox);
        form.Children.Add(Label("s"));
        _pageLabel = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 6),
            Foreground = LabelBrush()
        };
        form.Children.Add(_pageLabel);
        DockPanel.SetDock(form, Dock.Top);
        root.Children.Add(form);

        _formMessage = new TextBlock
        {
            Foreground = LabelBrush(),
            Margin = new Thickness(0, 0, 0, 8),
            TextWrapping = TextWrapping.Wrap
        };
        DockPanel.SetDock(_formMessage, Dock.Top);
        root.Children.Add(_formMessage);

        var tools = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        _toolButtons =
        [
            ToolButton("Punkt", Tool.Point),
            ToolButton("Linie", Tool.Line),
            ToolButton("Text", Tool.Text),
            ToolButton("Radierer", Tool.Eraser),
            ToolButton("Markieren", Tool.Mark)
        ];
        foreach (var button in _toolButtons)
        {
            tools.Children.Add(button);
        }

        tools.Children.Add(MakeButton("Kopieren", (_, _) => CopySelection()));
        tools.Children.Add(MakeButton("Einfügen", (_, _) => PasteSelection()));
        tools.Children.Add(MakeButton("Grafik speichern", (_, _) => SaveGraphic()));
        tools.Children.Add(MakeButton("Grafik holen", (_, _) => LoadGraphic()));
        tools.Children.Add(MakeButton("Rückgängig", (_, _) => Undo()));
        var invert = MakeButton("Invertieren", (_, _) => Invert());
        invert.ToolTip = "Gelbe LEDs werden schwarz und schwarze LEDs gelb. Ein markiertes Feld wird nur dort umgekehrt.";
        tools.Children.Add(invert);
        tools.Children.Add(Label("Schrift"));
        _fontBox = new ComboBox
        {
            Width = 150,
            Margin = new Thickness(0, 0, 8, 6),
            VerticalAlignment = VerticalAlignment.Center,
            Background = Brushes.White,
            Foreground = Brushes.Black,
            ItemsSource = _fonts,
            DisplayMemberPath = nameof(MobitecSignFont.ChoiceLabel),
            ItemContainerStyle = FontItemStyle()
        };
        _fontBox.SelectedItem = _fonts.FirstOrDefault(font => font.Id == 'i') ?? _fonts[0];
        _fontBox.SelectionChanged += (_, _) =>
        {
            if (!_suppressFontResize)
            {
                ResizeSelectedText();
            }
        };
        _fontBox.ToolTip = "Schriftgröße. Die drei genutzten Schriften stehen oben als Favoriten. Bei markiertem Text wird sofort neu gezeichnet.";
        tools.Children.Add(_fontBox);
        tools.Children.Add(Label("Text"));
        _textBox = FieldBox(string.Empty, 220);
        _textBox.ToolTip = "Zieltext. „Ziel einfügen“ setzt ihn mittig auf die aktive Anzeige, ein Klick mit Werkzeug Text setzt ihn an die Klickstelle.";
        _textBox.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter)
            {
                return;
            }

            PlaceTextCentered();
            e.Handled = true;
        };
        tools.Children.Add(_textBox);
        tools.Children.Add(MakeButton("Ziel einfügen", (_, _) => InsertDestination()));
        tools.Children.Add(Label("Linie"));
        _lineBox = FieldBox(program?.Ds001Value ?? string.Empty, 90);
        _lineBox.ToolTip = "Liniennummer der angezeigten Wechseltext-Seite. 1–3 Zeichen werden in 13×9 gesetzt, ab 4 Zeichen (z. B. RB33) in 10×7. Die Größe lässt sich danach ändern.";
        tools.Children.Add(_lineBox);
        tools.Children.Add(MakeButton("Linie einfügen", (_, _) => InsertLineNumber()));
        DockPanel.SetDock(tools, Dock.Top);
        root.Children.Add(tools);

        var buttons = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        var clear = MakeButton("Aktive Fläche löschen", (_, _) => ClearActive());
        DockPanel.SetDock(clear, Dock.Left);
        buttons.Children.Add(clear);
        var right = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        right.Children.Add(MakeButton("Schließen", (_, _) => Close()));
        right.Children.Add(MakeButton("Abbrechen", (_, _) => DiscardAndClose()));
        right.Children.Add(MakeButton("Neues Ziel", (_, _) => BeginNewDestination(), outlined: false));
        _saveButton = MakeButton("Speichern", (_, _) => SaveInPlace(), outlined: false);
        right.Children.Add(_saveButton);
        right.Children.Add(MakeButton("Übernehmen", (_, _) => Accept(), outlined: false));
        buttons.Children.Add(right);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        var signs = new StackPanel();
        foreach (var board in _boards)
        {
            signs.Children.Add(BuildSign(board));
        }

        root.Children.Add(signs);
        Content = root;

        SelectTool(Tool.Point);
        HighlightActive();
        ShowPage(0);
        _nameBox.TextChanged += (_, _) => MarkDirty();
        _numberBox.TextChanged += (_, _) => MarkDirty();
        _lineBox.TextChanged += (_, _) => MarkDirty();
        _intervalBox.TextChanged += (_, _) => MarkDirty();
        PreviewKeyDown += (_, e) =>
        {
            if (Keyboard.FocusedElement is TextBox or ComboBox)
            {
                return;
            }

            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Z)
            {
                Undo();
                e.Handled = true;
                return;
            }

            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.C)
            {
                CopySelection();
                e.Handled = true;
                return;
            }

            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.V)
            {
                PasteSelection();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Delete)
            {
                DeleteSelection();
                e.Handled = true;
                return;
            }

            var (dx, dy) = e.Key switch
            {
                Key.Left => (-1, 0),
                Key.Right => (1, 0),
                Key.Up => (0, -1),
                Key.Down => (0, 1),
                _ => (0, 0)
            };
            if (dx == 0 && dy == 0)
            {
                return;
            }

            if (NudgeSelection(dx, dy))
            {
                e.Handled = true;
            }
        };
    }

    public static MobitecGraphicEdit? Show(
        Window? owner,
        OutsideDisplayProgram? program,
        IEnumerable<int> usedDestinationNumbers,
        Action<MobitecGraphicEdit>? persist = null)
    {
        var fonts = MobitecSignFont.LoadEnabled();
        if (fonts.Count == 0)
        {
            throw new InvalidOperationException("Keine Mobitec-Schriften geladen.");
        }

        var dialog = new MobitecGraphicToolWindow(program, fonts, usedDestinationNumbers, persist);
        return DialogOwnerHelper.ShowOwnedDialog(dialog, owner) == true ? dialog._result : null;
    }

    private UIElement BuildSign(Board board)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 2) };
        board.TitleBlock = new TextBlock
        {
            Text = $"{board.Title}  {board.SizeLabel}",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 1),
            Foreground = LabelBrush()
        };
        panel.Children.Add(board.TitleBlock);
        board.Canvas.MouseLeftButtonDown += (_, e) => OnPress(board, e, erase: false);
        board.Canvas.MouseRightButtonDown += (_, e) => OnPress(board, e, erase: true);
        board.Canvas.MouseMove += (_, e) => OnMove(board, e);
        board.Canvas.MouseLeftButtonUp += (_, e) => OnRelease(board, e);
        board.Canvas.MouseRightButtonUp += (_, _) => EndDrag(board);
        board.Canvas.LostMouseCapture += (_, _) => _dragging = false;
        panel.Children.Add(board.Canvas);
        return panel;
    }

    private void OnPress(Board board, MouseButtonEventArgs e, bool erase)
    {
        _active = board;
        HighlightActive();
        if (!board.Canvas.TryCell(e.GetPosition(board.Canvas), out var col, out var row))
        {
            return;
        }

        if (_tool == Tool.Text && !erase)
        {
            PlaceText(board, _textBox.Text, col, row);
            return;
        }

        if (_tool == Tool.Mark && !erase)
        {
            _markCol = col;
            _markRow = row;
            _dragging = true;
            board.Canvas.CaptureMouse();
            board.Canvas.SetMarquee(col, row, 1, 1);
            return;
        }

        Touch(board);

        if (_tool == Tool.Line && !erase)
        {
            _lineCol = col;
            _lineRow = row;
            _dragging = true;
            board.Canvas.CaptureMouse();
            return;
        }

        if (_tool != Tool.Point && _tool != Tool.Eraser && !erase)
        {
            return;
        }

        ClearSelection();
        DropMarks(board);
        var paint = _tool == Tool.Eraser || erase;
        _eraseDrag = paint;
        _dragging = true;
        board.PushUndo();
        SetPixel(board, col, row, on: !paint);
        board.Canvas.CaptureMouse();
    }

    private void OnMove(Board board, MouseEventArgs e)
    {
        if (!_dragging || !ReferenceEquals(board, _active))
        {
            return;
        }

        if (_tool is Tool.Line or Tool.Mark)
        {
            if (_tool == Tool.Mark &&
                board.Canvas.TryCell(e.GetPosition(board.Canvas), out var markCol, out var markRow))
            {
                var left = Math.Min(_markCol, markCol);
                var top = Math.Min(_markRow, markRow);
                board.Canvas.SetMarquee(left, top, Math.Abs(markCol - _markCol) + 1, Math.Abs(markRow - _markRow) + 1);
            }

            return;
        }

        if (!board.Canvas.TryCell(e.GetPosition(board.Canvas), out var col, out var row))
        {
            return;
        }

        SetPixel(board, col, row, on: !_eraseDrag);
    }

    private void OnRelease(Board board, MouseButtonEventArgs e)
    {
        if (!_dragging || !ReferenceEquals(board, _active))
        {
            return;
        }

        if (_tool == Tool.Line &&
            board.Canvas.TryCell(e.GetPosition(board.Canvas), out var col, out var row) &&
            (col != _lineCol || row != _lineRow))
        {
            board.PushUndo();
            RememberSelectionBase(board);
            ApplyDrawnInk(board, TraceLine(_lineCol, _lineRow, col, row));
        }
        else if (_tool == Tool.Mark)
        {
            board.Canvas.ClearMarquee();
            var endX = _markCol;
            var endY = _markRow;
            if (board.Canvas.TryCell(e.GetPosition(board.Canvas), out var endCol, out var endRow))
            {
                endX = endCol;
                endY = endRow;
            }

            FinishMarquee(board, endX, endY);
        }

        EndDrag(board);
    }

    private void EndDrag(Board board)
    {
        _dragging = false;
        if (board.Canvas.IsMouseCaptured)
        {
            board.Canvas.ReleaseMouseCapture();
        }
    }

    private void PlaceTextCentered() => PlaceText(_active, _textBox.Text, left: null, anchorRow: null);

    private void InsertDestination()
    {
        var board = _active.Address == MobitecBitmapFrame.LineAddress ? _boards[0] : _active;
        _active = board;
        HighlightActive();
        PlaceText(board, _textBox.Text, left: null, anchorRow: null);
    }

    private void InsertLineNumber()
    {
        var text = _lineBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            text = _textBox.Text.Trim();
            if (!string.IsNullOrWhiteSpace(text))
            {
                _lineBox.Text = text;
            }
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            _formMessage.Text = "Bitte die Liniennummer ins Feld „Linie“ schreiben.";
            _lineBox.Focus();
            Keyboard.Focus(_lineBox);
            return;
        }

        var board = _boards.First(item => item.Address == MobitecBitmapFrame.LineAddress);
        _active = board;
        HighlightActive();
        var font = FontForLineNumber(text);
        if (!PlaceText(board, text, left: null, anchorRow: null, font, clearPage: true))
        {
            return;
        }

        ShowLineFont(font);
        _formMessage.Text = board.PageCount > 1
            ? $"Linie {text} auf Seite {board.PageSlot + 1}. Schrift {font.Label}."
            : $"Linie {text} auf der Linienanzeige. Schrift {font.Label}.";
    }

    /// <summary>1–3 Zeichen in 13×9 (i), ab 4 Zeichen in 10×7 (l). Eine spätere Auswahl bleibt stehen.</summary>
    private MobitecSignFont FontForLineNumber(string text)
    {
        var id = text.Length >= 4 ? 'l' : 'i';
        return _fonts.FirstOrDefault(font => font.Id == id) ?? SelectedFont();
    }

    private void ShowLineFont(MobitecSignFont font)
    {
        if (ReferenceEquals(_fontBox.SelectedItem, font))
        {
            return;
        }

        _suppressFontResize = true;
        _fontBox.SelectedItem = font;
        _suppressFontResize = false;
    }

    /// <summary>
    /// Setzt den Text so, dass die ganze Zeichenhöhe in den 16 Zeilen bleibt.
    /// Ein Klick ist die Mitte der Schrift, nicht die Oberkante.
    /// </summary>
    private bool PlaceText(Board board, string text, int? left, int? anchorRow, MobitecSignFont? font = null, bool clearPage = false)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            _formMessage.Text = "Bitte zuerst den Text in das Feld „Text“ schreiben.";
            FocusTextBox();
            return false;
        }

        Touch(board);

        font ??= SelectedFont();
        var textWidth = font.MeasureWidth(text);
        var textHeight = font.MeasureHeight(text);
        if (textWidth <= 0 || textHeight <= 0)
        {
            _formMessage.Text = "Diese Schrift enthält keines der eingegebenen Zeichen.";
            FocusTextBox();
            return false;
        }

        _formMessage.Text = string.Empty;
        int x;
        if (left is int column)
        {
            x = column;
            if (textWidth < board.Width && x > board.Width - textWidth)
            {
                x = board.Width - textWidth;
            }

            if (x < 0)
            {
                x = 0;
            }
        }
        else
        {
            x = Math.Max(0, (board.Width - textWidth) / 2);
        }

        var maxTop = Math.Max(0, MobitecBitmapFrame.Height - textHeight);
        var top = anchorRow is int row
            ? row - textHeight / 2
            : (MobitecBitmapFrame.Height - textHeight) / 2;
        if (top < 0)
        {
            top = 0;
        }
        else if (top > maxTop)
        {
            top = maxTop;
        }

        board.PushUndo();
        if (clearPage)
        {
            Array.Clear(board.Pixels);
            DropMarks(board);
        }

        RememberSelectionBase(board);
        var mask = new bool[MobitecBitmapFrame.Height, board.Width];
        font.DrawTop(mask, text, x, top);
        var ink = new List<(int X, int Y)>();
        for (var py = 0; py < MobitecBitmapFrame.Height; py++)
        {
            for (var px = 0; px < board.Width; px++)
            {
                if (mask[py, px])
                {
                    ink.Add((px, py));
                }
            }
        }

        ApplyDrawnInk(board, ink);
        if (_activeMark is not null)
        {
            _activeMark.Text = text;
            _activeMark.FontId = font.Id;
        }

        return true;
    }

    /// <summary>Zeichnet den markierten Text in der neu gewählten Schrift an derselben Stelle neu.</summary>
    private void ResizeSelectedText()
    {
        if (_activeMark?.Text is not { Length: > 0 } text ||
            _selectionBoard is null ||
            _selectionBase is null ||
            _selectionInk.Count == 0 ||
            !ReferenceEquals(_activeMark.Board, _selectionBoard) ||
            _activeMark.Page != _selectionPage ||
            _selectionBoard.IsBlankShown ||
            _selectionBoard.PageSlot != _selectionPage)
        {
            return;
        }

        var font = SelectedFont();
        if (font.Id == _activeMark.FontId)
        {
            return;
        }

        var textWidth = font.MeasureWidth(text);
        var textHeight = font.MeasureHeight(text);
        if (textWidth <= 0 || textHeight <= 0)
        {
            _formMessage.Text = "Diese Schrift enthält keines der eingegebenen Zeichen.";
            return;
        }

        var left = int.MaxValue;
        var top = int.MaxValue;
        foreach (var (x, y) in _selectionInk)
        {
            var nx = x + _selectionShiftX;
            var ny = y + _selectionShiftY;
            if (nx < left)
            {
                left = nx;
            }

            if (ny < top)
            {
                top = ny;
            }
        }

        var board = _selectionBoard;
        if (left < 0)
        {
            left = 0;
        }

        if (textWidth < board.Width && left > board.Width - textWidth)
        {
            left = board.Width - textWidth;
        }

        var maxTop = Math.Max(0, MobitecBitmapFrame.Height - textHeight);
        if (top < 0)
        {
            top = 0;
        }
        else if (top > maxTop)
        {
            top = maxTop;
        }

        board.PushUndo();
        var mask = new bool[MobitecBitmapFrame.Height, board.Width];
        font.DrawTop(mask, text, left, top);
        Array.Copy(_selectionBase, board.Pixels, _selectionBase.Length);
        _selectionInk.Clear();
        _selectionShiftX = 0;
        _selectionShiftY = 0;
        var pixels = board.Pixels;
        var rows = pixels.GetLength(0);
        var cols = pixels.GetLength(1);
        for (var y = 0; y < rows; y++)
        {
            for (var x = 0; x < cols; x++)
            {
                if (!mask[y, x])
                {
                    continue;
                }

                pixels[y, x] = true;
                _selectionInk.Add((x, y));
            }
        }

        _activeMark.Ink.Clear();
        _activeMark.Ink.AddRange(_selectionInk);
        _activeMark.FontId = font.Id;
        _selectionMoveUndo = false;
        ShowSelectionFrame();
        _formMessage.Text = $"Schrift {font.Label}.";
    }

    /// <summary>
    /// Merkt sich die Fläche vor dem Einfügen. Die Markierung enthält danach jeden Punkt der neuen
    /// Schrift, auch wo sie über bereits leuchtenden Pixeln liegt.
    /// </summary>
    private void RememberSelectionBase(Board board)
    {
        foreach (var other in _boards)
        {
            other.Canvas.ClearSelection();
        }

        _selectionBoard = board;
        _selectionPage = board.PageSlot;
        _selectionBase = MobitecBitmapFrame.Clone(board.Pixels);
        _selectionInk.Clear();
        _selectionShiftX = 0;
        _selectionShiftY = 0;
    }

    /// <summary>Setzt die gezeichneten Punkte und nimmt sie vollständig in die Markierung auf.</summary>
    private void ApplyDrawnInk(Board board, IReadOnlyList<(int X, int Y)> ink)
    {
        if (!ReferenceEquals(_selectionBoard, board) || ink.Count == 0)
        {
            ClearSelection();
            return;
        }

        var pixels = board.Pixels;
        var rows = pixels.GetLength(0);
        var cols = pixels.GetLength(1);
        foreach (var (x, y) in ink)
        {
            if (x < 0 || y < 0 || x >= cols || y >= rows)
            {
                continue;
            }

            pixels[y, x] = true;
            _selectionInk.Add((x, y));
        }

        if (_selectionInk.Count == 0)
        {
            ClearSelection();
            return;
        }

        _selectionMoveUndo = false;
        _activeMark = AddMark(board, _selectionInk);
        ShowSelectionFrame();
        Keyboard.Focus(this);
    }

    /// <summary>
    /// Klick auf eine schon gesetzte Zahl oder ein Ziel: nur den Rahmen setzen, nichts neu zeichnen.
    /// </summary>
    private bool TryReselect(Board board, int col, int row)
    {
        var mark = FindMark(board, col, row);
        if (mark is not null)
        {
            SelectInk(board, mark.Ink, mark);
            return true;
        }

        var cluster = FindCluster(board.Pixels, col, row);
        if (cluster is null)
        {
            return false;
        }

        SelectInk(board, cluster, mark: null);
        return true;
    }

    private PlacedMark? FindMark(Board board, int col, int row)
    {
        PlacedMark? inside = null;
        var insideArea = int.MaxValue;
        for (var i = _marks.Count - 1; i >= 0; i--)
        {
            var mark = _marks[i];
            if (!ReferenceEquals(mark.Board, board) || mark.Page != board.PageSlot || mark.Ink.Count == 0)
            {
                continue;
            }

            var minX = int.MaxValue;
            var minY = int.MaxValue;
            var maxX = int.MinValue;
            var maxY = int.MinValue;
            var exact = false;
            foreach (var (x, y) in mark.Ink)
            {
                if (x == col && y == row)
                {
                    exact = true;
                }

                if (x < minX)
                {
                    minX = x;
                }

                if (y < minY)
                {
                    minY = y;
                }

                if (x > maxX)
                {
                    maxX = x;
                }

                if (y > maxY)
                {
                    maxY = y;
                }
            }

            if (exact)
            {
                return mark;
            }

            var area = (maxX - minX + 1) * (maxY - minY + 1);
            if (col >= minX && col <= maxX && row >= minY && row <= maxY && area < insideArea)
            {
                inside = mark;
                insideArea = area;
            }
        }

        return inside;
    }

    private void SelectInk(Board board, IReadOnlyList<(int X, int Y)> ink, PlacedMark? mark)
    {
        var basePixels = MobitecBitmapFrame.Clone(board.Pixels);
        var rows = basePixels.GetLength(0);
        var cols = basePixels.GetLength(1);
        foreach (var (x, y) in ink)
        {
            if (x >= 0 && y >= 0 && x < cols && y < rows)
            {
                basePixels[y, x] = false;
            }
        }

        foreach (var other in _boards)
        {
            other.Canvas.ClearSelection();
        }

        _selectionBoard = board;
        _selectionPage = board.PageSlot;
        _selectionBase = basePixels;
        _selectionInk.Clear();
        _selectionInk.AddRange(ink);
        _selectionShiftX = 0;
        _selectionShiftY = 0;
        _selectionMoveUndo = false;
        _activeMark = mark ?? AddMark(board, ink);
        ShowSelectionFrame();
        Keyboard.Focus(this);
    }

    private List<(int X, int Y)>? FindCluster(bool[,] pixels, int col, int row)
    {
        var rows = pixels.GetLength(0);
        var cols = pixels.GetLength(1);
        if (!TryFindSeed(pixels, cols, rows, col, row, out var seedCol, out var seedRow))
        {
            return null;
        }

        var top = seedRow;
        var bottom = seedRow;
        while (top > 0 && pixels[top - 1, seedCol])
        {
            top--;
        }

        while (bottom < rows - 1 && pixels[bottom + 1, seedCol])
        {
            bottom++;
        }

        var include = new bool[cols];
        include[seedCol] = true;
        GrowCluster(pixels, rows, cols, seedCol, -1, ref top, ref bottom, include);
        GrowCluster(pixels, rows, cols, seedCol, 1, ref top, ref bottom, include);

        var ink = new List<(int X, int Y)>();
        for (var x = 0; x < cols; x++)
        {
            if (!include[x])
            {
                continue;
            }

            for (var y = top; y <= bottom; y++)
            {
                if (pixels[y, x])
                {
                    ink.Add((x, y));
                }
            }
        }

        return ink.Count == 0 ? null : ink;
    }

    private static bool TryFindSeed(bool[,] pixels, int cols, int rows, int col, int row, out int seedCol, out int seedRow)
    {
        seedCol = col;
        seedRow = row;
        if ((uint)col < (uint)cols && (uint)row < (uint)rows && pixels[row, col])
        {
            return true;
        }

        for (var dx = -2; dx <= 2; dx++)
        {
            if (dx == 0)
            {
                continue;
            }

            var x = col + dx;
            if ((uint)x < (uint)cols && (uint)row < (uint)rows && pixels[row, x])
            {
                seedCol = x;
                seedRow = row;
                return true;
            }
        }

        return false;
    }

    private static void GrowCluster(
        bool[,] pixels,
        int rows,
        int cols,
        int seedCol,
        int step,
        ref int top,
        ref int bottom,
        bool[] include)
    {
        var empty = 0;
        for (var x = seedCol + step; x >= 0 && x < cols; x += step)
        {
            if (!ColumnHits(pixels, rows, x, top, bottom))
            {
                empty++;
                if (empty > 8)
                {
                    break;
                }

                continue;
            }

            include[x] = true;
            empty = 0;
            for (var y = 0; y < rows; y++)
            {
                if (!pixels[y, x] || y < top - 1 || y > bottom + 1)
                {
                    continue;
                }

                if (y < top)
                {
                    top = y;
                }

                if (y > bottom)
                {
                    bottom = y;
                }
            }
        }
    }

    private static bool ColumnHits(bool[,] pixels, int rows, int column, int top, int bottom)
    {
        var from = Math.Max(0, top - 1);
        var to = Math.Min(rows - 1, bottom + 1);
        for (var y = from; y <= to; y++)
        {
            if (pixels[y, column])
            {
                return true;
            }
        }

        return false;
    }

    private void ShowSelectionFrame()
    {
        if (_selectionBoard is null || _selectionInk.Count == 0)
        {
            return;
        }

        var minX = int.MaxValue;
        var minY = int.MaxValue;
        var maxX = int.MinValue;
        var maxY = int.MinValue;
        foreach (var (x, y) in _selectionInk)
        {
            var nx = x + _selectionShiftX;
            var ny = y + _selectionShiftY;
            if (nx < minX)
            {
                minX = nx;
            }

            if (ny < minY)
            {
                minY = ny;
            }

            if (nx > maxX)
            {
                maxX = nx;
            }

            if (ny > maxY)
            {
                maxY = ny;
            }
        }

        if (_hasField)
        {
            _selectionBoard.Canvas.SetSelection(
                _fieldX + _selectionShiftX,
                _fieldY + _selectionShiftY,
                _fieldW,
                _fieldH);
            return;
        }

        _selectionBoard.Canvas.SetSelection(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    private PlacedMark AddMark(Board board, IReadOnlyList<(int X, int Y)> ink)
    {
        var mark = new PlacedMark(board, board.PageSlot, ink.ToList());
        _marks.Add(mark);
        return mark;
    }

    private void DropMarks(Board board)
    {
        _marks.RemoveAll(mark => ReferenceEquals(mark.Board, board) && mark.Page == board.PageSlot);
        if (_activeMark is not null &&
            ReferenceEquals(_activeMark.Board, board) &&
            _activeMark.Page == board.PageSlot)
        {
            _activeMark = null;
        }
    }

    private bool NudgeSelection(int dx, int dy)
    {
        if (_dragging || _selectionBoard is null || _selectionBase is null || _selectionInk.Count == 0)
        {
            return false;
        }

        if (_selectionBoard.IsBlankShown || _selectionBoard.PageSlot != _selectionPage)
        {
            return false;
        }

        var nextX = _selectionShiftX + dx;
        var nextY = _selectionShiftY + dy;
        var pixels = _selectionBoard.Pixels;
        var rows = pixels.GetLength(0);
        var cols = pixels.GetLength(1);
        var minX = cols;
        var minY = rows;
        var maxX = -1;
        var maxY = -1;
        foreach (var (x, y) in _selectionInk)
        {
            var nx = x + nextX;
            var ny = y + nextY;
            if (nx < 0 || ny < 0 || nx >= cols || ny >= rows)
            {
                return false;
            }

            if (_hasField)
            {
                var fieldX = _fieldX + nextX;
                var fieldY = _fieldY + nextY;
                if (fieldX < 0 || fieldY < 0 || fieldX + _fieldW > cols || fieldY + _fieldH > rows)
                {
                    return false;
                }
            }

            if (nx < minX)
            {
                minX = nx;
            }

            if (ny < minY)
            {
                minY = ny;
            }

            if (nx > maxX)
            {
                maxX = nx;
            }

            if (ny > maxY)
            {
                maxY = ny;
            }
        }

        if (!_selectionMoveUndo)
        {
            _selectionBoard.PushUndo();
            _selectionMoveUndo = true;
        }

        _selectionShiftX = nextX;
        _selectionShiftY = nextY;
        Array.Copy(_selectionBase, pixels, _selectionBase.Length);
        foreach (var (x, y) in _selectionInk)
        {
            pixels[y + nextY, x + nextX] = true;
        }

        if (_activeMark is not null)
        {
            _activeMark.Ink.Clear();
            foreach (var (x, y) in _selectionInk)
            {
                _activeMark.Ink.Add((x + nextX, y + nextY));
            }
        }

        ShowSelectionFrame();
        return true;
    }

    private void ClearSelection()
    {
        _selectionBoard = null;
        _selectionBase = null;
        _selectionInk.Clear();
        _selectionShiftX = 0;
        _selectionShiftY = 0;
        _selectionMoveUndo = false;
        _activeMark = null;
        _hasField = false;
        foreach (var board in _boards)
        {
            board.Canvas.ClearSelection();
        }
    }

    private void FinishMarquee(Board board, int col, int row)
    {
        if (board.IsBlankShown)
        {
            _formMessage.Text = "Auf dieser Seite ist nichts zum Markieren.";
            return;
        }

        if (_markCol == col && _markRow == row)
        {
            if (!TryReselect(board, col, row))
            {
                _formMessage.Text = "An dieser Stelle liegt kein Text. Ziehe ein Feld über die Zahl oder das Ziel.";
            }

            return;
        }

        var left = Math.Min(_markCol, col);
        var top = Math.Min(_markRow, row);
        var right = Math.Max(_markCol, col);
        var bottom = Math.Max(_markRow, row);
        var ink = new List<(int X, int Y)>();
        for (var y = top; y <= bottom; y++)
        {
            for (var x = left; x <= right; x++)
            {
                if (board.Pixels[y, x])
                {
                    ink.Add((x, y));
                }
            }
        }

        if (ink.Count == 0)
        {
            _formMessage.Text = "In dem Feld ist nichts gezeichnet.";
            return;
        }

        SelectInk(board, ink, mark: null);
        _hasField = true;
        _fieldX = left;
        _fieldY = top;
        _fieldW = right - left + 1;
        _fieldH = bottom - top + 1;
        ShowSelectionFrame();
        _formMessage.Text = $"Feld {_fieldW}×{_fieldH} markiert. Kopieren übernimmt genau dieses Feld.";
    }

    private void CopySelection()
    {
        if (!TryCaptureField(out var width, out var height, out var ink))
        {
            _formMessage.Text = "Zuerst mit Markieren ein Feld aufziehen.";
            return;
        }

        _clipboardW = width;
        _clipboardH = height;
        _clipboard = ink;
        _formMessage.Text = $"Feld {_clipboardW}×{_clipboardH} kopiert. Der Rahmen zeigt genau diesen Bereich.";
    }

    private void SaveGraphic()
    {
        if (!TryCaptureField(out var width, out var height, out var ink))
        {
            _formMessage.Text = "Zuerst mit Markieren ein Feld aufziehen.";
            return;
        }

        var name = PromptTextDialog.Ask(this, "Grafik speichern", "Name, unter dem die Grafik wieder abrufbar ist.");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        if (!MobitecGraphicLibrary.IsValidName(name))
        {
            _formMessage.Text = "Der Name darf höchstens 80 Zeichen haben und keines von \\ / : * ? \" < > |.";
            return;
        }

        var folder = MobitecGraphicLibrary.Folder;
        if (MobitecGraphicLibrary.Exists(folder, name) &&
            MessageBox.Show(
                this,
                $"„{name.Trim()}“ gibt es schon. Überschreiben?",
                "Grafik speichern",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        MobitecGraphicLibrary.Save(folder, name, width, height, ink);
        _formMessage.Text = $"Grafik „{name.Trim()}“ gespeichert. „Grafik holen“ setzt sie wieder auf die aktive Anzeige.";
    }

    private void LoadGraphic()
    {
        var graphic = MobitecGraphicPickerDialog.Pick(this, MobitecGraphicLibrary.Folder);
        if (graphic is null)
        {
            return;
        }

        if (graphic.Width > _active.Width || graphic.Height > MobitecBitmapFrame.Height)
        {
            _formMessage.Text = $"„{graphic.Name}“ ist {graphic.Width}×{graphic.Height} und passt nicht auf {_active.Title} ({_active.Width}×16).";
            return;
        }

        _clipboard = graphic.Points.ToList();
        _clipboardW = graphic.Width;
        _clipboardH = graphic.Height;
        if (PasteSelection())
        {
            _formMessage.Text = $"„{graphic.Name}“ sitzt oben links auf {_active.Title}. Pfeiltasten verschieben die Grafik.";
        }
    }

    private bool TryCaptureField(out int width, out int height, out List<(int X, int Y)> ink)
    {
        width = 0;
        height = 0;
        ink = [];
        if (_selectionBoard is null || _selectionInk.Count == 0)
        {
            return false;
        }

        var originX = _hasField ? _fieldX + _selectionShiftX : int.MaxValue;
        var originY = _hasField ? _fieldY + _selectionShiftY : int.MaxValue;
        if (!_hasField)
        {
            var maxX = int.MinValue;
            var maxY = int.MinValue;
            foreach (var (x, y) in _selectionInk)
            {
                var nx = x + _selectionShiftX;
                var ny = y + _selectionShiftY;
                originX = Math.Min(originX, nx);
                originY = Math.Min(originY, ny);
                maxX = Math.Max(maxX, nx);
                maxY = Math.Max(maxY, ny);
            }

            width = maxX - originX + 1;
            height = maxY - originY + 1;
        }
        else
        {
            width = _fieldW;
            height = _fieldH;
        }

        foreach (var (x, y) in _selectionInk)
        {
            ink.Add((x + _selectionShiftX - originX, y + _selectionShiftY - originY));
        }

        return width > 0 && height > 0 && ink.Count > 0;
    }

    private bool PasteSelection()
    {
        if (_clipboard is null || _clipboard.Count == 0)
        {
            _formMessage.Text = "Noch nichts kopiert.";
            return false;
        }

        var board = _active;
        Touch(board);
        if (board.IsBlankShown)
        {
            return false;
        }

        board.PushUndo();
        RememberSelectionBase(board);
        var pixels = board.Pixels;
        var rows = pixels.GetLength(0);
        var cols = pixels.GetLength(1);
        foreach (var (x, y) in _clipboard)
        {
            if (x < 0 || y < 0 || x >= cols || y >= rows)
            {
                continue;
            }

            pixels[y, x] = true;
            _selectionInk.Add((x, y));
        }

        if (_selectionInk.Count == 0)
        {
            ClearSelection();
            _formMessage.Text = "Das kopierte Feld passt nicht auf diese Anzeige.";
            return false;
        }

        _selectionMoveUndo = false;
        _activeMark = AddMark(board, _selectionInk);
        _hasField = _clipboardW > 0 && _clipboardH > 0;
        _fieldX = 0;
        _fieldY = 0;
        _fieldW = _clipboardW;
        _fieldH = _clipboardH;
        ShowSelectionFrame();
        Keyboard.Focus(this);
        _formMessage.Text = $"Feld {_clipboardW}×{_clipboardH} eingefügt. Der Rahmen zeigt den kopierten Bereich.";
        return true;
    }

    private void FocusTextBox()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            _textBox.Focus();
            Keyboard.Focus(_textBox);
        }));
    }

    private void Invert()
    {
        if (TryInvertField())
        {
            return;
        }

        if (_active.IsBlankShown)
        {
            _formMessage.Text = "Auf dieser Seite ist nichts zu invertieren.";
            return;
        }

        ClearSelection();
        DropMarks(_active);
        Touch(_active);
        _active.PushUndo();
        Flip(_active.Pixels, 0, 0, _active.Pixels.GetLength(1), _active.Pixels.GetLength(0));
        _active.Canvas.InvalidateVisual();
        MarkDirty();
        _formMessage.Text = "Aktive Fläche invertiert.";
    }

    private bool TryInvertField()
    {
        if (!_hasField ||
            _selectionBoard is null ||
            _selectionBoard.IsBlankShown ||
            _selectionBoard.PageSlot != _selectionPage)
        {
            return false;
        }

        var board = _selectionBoard;
        Touch(board);
        board.PushUndo();
        var left = _fieldX + _selectionShiftX;
        var top = _fieldY + _selectionShiftY;
        Flip(board.Pixels, left, top, _fieldW, _fieldH);
        _fieldX = left;
        _fieldY = top;
        _selectionShiftX = 0;
        _selectionShiftY = 0;
        _selectionMoveUndo = false;
        _selectionInk.Clear();
        var pixels = board.Pixels;
        var rows = pixels.GetLength(0);
        var cols = pixels.GetLength(1);
        for (var y = top; y < top + _fieldH; y++)
        {
            for (var x = left; x < left + _fieldW; x++)
            {
                if (x >= 0 && y >= 0 && x < cols && y < rows && pixels[y, x])
                {
                    _selectionInk.Add((x, y));
                }
            }
        }

        _selectionBase = MobitecBitmapFrame.Clone(pixels);
        DropMarks(board);
        board.Canvas.InvalidateVisual();
        board.Canvas.SetSelection(left, top, _fieldW, _fieldH);
        MarkDirty();
        _formMessage.Text = "Markiertes Feld invertiert.";
        return true;
    }

    private static void Flip(bool[,] pixels, int left, int top, int width, int height)
    {
        var rows = pixels.GetLength(0);
        var cols = pixels.GetLength(1);
        for (var y = top; y < top + height; y++)
        {
            for (var x = left; x < left + width; x++)
            {
                if (x >= 0 && y >= 0 && x < cols && y < rows)
                {
                    pixels[y, x] = !pixels[y, x];
                }
            }
        }
    }

    private void ClearActive()
    {
        if (_active.IsBlankShown)
        {
            return;
        }

        ClearSelection();
        DropMarks(_active);
        Touch(_active);
        _active.PushUndo();
        Array.Clear(_active.Pixels);
        _active.Canvas.InvalidateVisual();
    }

    private void Undo()
    {
        if (_active.IsBlankShown || _active.Undo.Count == 0)
        {
            return;
        }

        ClearSelection();
        DropMarks(_active);
        var previous = _active.Undo[^1];
        _active.Undo.RemoveAt(_active.Undo.Count - 1);
        Array.Copy(previous, _active.Pixels, previous.Length);
        _active.Canvas.InvalidateVisual();
        MarkDirty();
    }

    private void Touch(Board board)
    {
        if (board.Materialize(_page))
        {
            RefreshPageBar();
        }
    }

    private int PageCount() => Math.Max(1, _boards.Max(board => board.PageCount));

    private void ShowPage(int index)
    {
        _dragging = false;
        ClearSelection();
        _page = Math.Clamp(index, 0, PageCount() - 1);
        foreach (var board in _boards)
        {
            board.Show(_page);
        }

        RefreshPageBar();
    }

    private void AddPage()
    {
        if (PageCount() >= 8)
        {
            _formMessage.Text = "Höchstens 8 Wechseltext-Seiten.";
            return;
        }

        var next = PageCount();
        foreach (var board in _boards)
        {
            board.AppendTowards(next);
        }

        _formMessage.Text = string.Empty;
        MarkDirty();
        ShowPage(next);
    }

    private void RemovePage()
    {
        if (PageCount() <= 1)
        {
            _formMessage.Text = "Es gibt nur eine Seite.";
            return;
        }

        foreach (var board in _boards)
        {
            board.RemoveAt(_page);
        }

        _formMessage.Text = string.Empty;
        MarkDirty();
        ShowPage(Math.Min(_page, PageCount() - 1));
    }

    private void RefreshPageBar()
    {
        _pageButtons.Children.Clear();
        var count = PageCount();
        for (var i = 0; i < count; i++)
        {
            var index = i;
            var button = MakeButton((i + 1).ToString(), (_, _) => ShowPage(index));
            if (i == _page)
            {
                button.Background = Amber;
                button.Foreground = Brushes.Black;
            }

            _pageButtons.Children.Add(button);
        }

        _pageLabel.Text = count <= 1
            ? "Eine Seite."
            : $"Seite {_page + 1} von {count}";
    }

    private bool TryReadInterval(out int interval)
    {
        interval = 3;
        if (!int.TryParse(_intervalBox.Text.Trim(), out var seconds) || seconds is < 1 or > 99)
        {
            _formMessage.Text = "Der Takt muss zwischen 1 und 99 Sekunden liegen.";
            return false;
        }

        interval = seconds;
        return true;
    }

    private void BeginNewDestination()
    {
        var number = NextFreeDestinationNumber();
        if (number is null)
        {
            _formMessage.Text = "Es ist keine freie Zielnummer zwischen 1 und 9999 mehr übrig.";
            return;
        }

        _createNew = true;
        _nameBox.Text = string.Empty;
        _lineBox.Text = string.Empty;
        _textBox.Text = string.Empty;
        _numberBox.Text = number.Value.ToString();
        _intervalBox.Text = "3";
        foreach (var board in _boards)
        {
            DropMarks(board);
            board.ResetBlank();
        }

        ShowPage(0);
        _formMessage.Text = $"Neues Ziel {number.Value}. Name eintragen und mit Speichern sichern.";
        _nameBox.Focus();
        Keyboard.Focus(_nameBox);
    }

    /// <summary>Nächste freie Nummer hinter der höchsten vergebenen, sonst die kleinste Lücke.</summary>
    private int? NextFreeDestinationNumber()
    {
        if (_usedDestinationNumbers.Count == 0)
        {
            return 1;
        }

        var afterMax = _usedDestinationNumbers.Max() + 1;
        if (afterMax is >= 1 and <= 9999)
        {
            return afterMax;
        }

        for (var candidate = 1; candidate <= 9999; candidate++)
        {
            if (!_usedDestinationNumbers.Contains(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private void Accept()
    {
        if (!TryBuildEdit(out var edit))
        {
            return;
        }

        _result = edit;
        _allowClose = true;
        DialogResult = true;
        Close();
    }

    private void DiscardAndClose()
    {
        _allowClose = true;
        DialogResult = false;
        Close();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_allowClose && _dirty)
        {
            var answer = MessageBox.Show(
                this,
                "Die Anzeige wurde geändert. Möchten Sie vor dem Schließen speichern?",
                "Mobitec Grafiktool",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);
            if (answer == MessageBoxResult.Cancel)
            {
                e.Cancel = true;
                return;
            }

            if (answer == MessageBoxResult.Yes && !SaveInPlace(announce: false))
            {
                e.Cancel = true;
                return;
            }
        }

        base.OnClosing(e);
    }

    /// <summary>Schreibt Front, Seite und Linie mit allen Wechsel-Seiten und lässt das Fenster offen.</summary>
    private bool SaveInPlace(bool announce = true)
    {
        if (_persist is null)
        {
            _formMessage.Text = "Speichern ist in diesem Fenster nicht verbunden.";
            return false;
        }

        if (!TryBuildEdit(out var edit))
        {
            return false;
        }

        try
        {
            _persist(edit);
        }
        catch (Exception ex)
        {
            _formMessage.Text = "Speichern fehlgeschlagen: " + ex.Message;
            return false;
        }

        _result = edit;
        _createNew = false;
        if (edit.DestinationNumber is int number)
        {
            _ownDestinationNumber = number;
            _usedDestinationNumbers.Add(number);
        }

        _dirty = false;
        if (announce)
        {
            _formMessage.Text = "Gespeichert. Alle Seiten sind in der Datei, das Fenster bleibt offen.";
            PaintSaveButtonSaved();
        }

        return true;
    }

    private void MarkDirty()
    {
        _dirty = true;
        ResetSaveButton();
    }

    private void PaintSaveButtonSaved()
    {
        if (_saveButton is null)
        {
            return;
        }

        _saveButton.SetResourceReference(BackgroundProperty, "SmartSuccessOnDarkBackgroundBrush");
        _saveButton.SetResourceReference(BorderBrushProperty, "SmartSuccessOnDarkBorderBrush");
        _saveButton.SetResourceReference(ForegroundProperty, "SmartSuccessOnDarkForegroundBrush");
    }

    private void ResetSaveButton()
    {
        if (_saveButton is null)
        {
            return;
        }

        _saveButton.ClearValue(BackgroundProperty);
        _saveButton.ClearValue(BorderBrushProperty);
        _saveButton.Foreground = UiTheme.OnPrimaryBrush;
    }

    private bool TryBuildEdit(out MobitecGraphicEdit edit)
    {
        edit = null!;
        if (!TryReadForm(_createNew, out var name, out var line, out var number))
        {
            return false;
        }

        if (!TryReadInterval(out var interval))
        {
            return false;
        }

        edit = new MobitecGraphicEdit(
            _boards[0].ToFrame(interval),
            _boards[1].ToFrame(interval),
            _boards[2].ToFrame(interval),
            _createNew,
            name,
            line,
            number,
            interval);
        return true;
    }

    private void DeleteSelection()
    {
        if (_selectionBoard is null ||
            _selectionInk.Count == 0 ||
            _selectionBoard.IsBlankShown ||
            _selectionBoard.PageSlot != _selectionPage)
        {
            _formMessage.Text = "Nichts markiert.";
            return;
        }

        var board = _selectionBoard;
        board.PushUndo();
        var pixels = board.Pixels;
        var rows = pixels.GetLength(0);
        var cols = pixels.GetLength(1);
        if (_hasField)
        {
            var left = _fieldX + _selectionShiftX;
            var top = _fieldY + _selectionShiftY;
            for (var y = top; y < top + _fieldH; y++)
            {
                for (var x = left; x < left + _fieldW; x++)
                {
                    if (x >= 0 && y >= 0 && x < cols && y < rows)
                    {
                        pixels[y, x] = false;
                    }
                }
            }
        }
        else
        {
            foreach (var (x, y) in _selectionInk)
            {
                var nx = x + _selectionShiftX;
                var ny = y + _selectionShiftY;
                if (nx >= 0 && ny >= 0 && nx < cols && ny < rows)
                {
                    pixels[ny, nx] = false;
                }
            }
        }

        ClearSelection();
        DropMarks(board);
        board.Canvas.InvalidateVisual();
        _formMessage.Text = "Markierte Fläche gelöscht.";
    }

    private bool TryReadForm(bool createNew, out string name, out string line, out int? number)
    {
        name = _nameBox.Text.Trim();
        line = _lineBox.Text.Trim();
        number = null;
        if (createNew && name.Length == 0)
        {
            _formMessage.Text = "Bitte einen Namen für das neue Ziel eingeben.";
            return false;
        }

        var raw = _numberBox.Text.Trim();
        if (raw.Length > 0)
        {
            if (!int.TryParse(raw, out var parsed) || parsed is < 0 or > 9999)
            {
                _formMessage.Text = "Die Zielnummer muss zwischen 0 und 9999 liegen.";
                return false;
            }

            var keepsOwnNumber = !createNew && parsed == _ownDestinationNumber;
            if (_usedDestinationNumbers.Contains(parsed) && !keepsOwnNumber)
            {
                _formMessage.Text = "Diese Zielnummer ist schon vergeben.";
                return false;
            }

            number = parsed;
        }

        _formMessage.Text = string.Empty;
        return true;
    }

    private void SelectTool(Tool tool)
    {
        _tool = tool;
        for (var i = 0; i < _toolButtons.Length; i++)
        {
            var on = (Tool)i == tool;
            _toolButtons[i].Background = on ? Amber : Brushes.Transparent;
            _toolButtons[i].Foreground = on ? Brushes.Black : LabelBrush();
        }

        if (tool == Tool.Text)
        {
            FocusTextBox();
        }
    }

    private void HighlightActive()
    {
        foreach (var board in _boards)
        {
            board.Canvas.Active = ReferenceEquals(board, _active);
            board.Canvas.InvalidateVisual();
        }
    }

    private MobitecSignFont SelectedFont() =>
        _fontBox.SelectedItem as MobitecSignFont ?? _fonts[0];

    private Button ToolButton(string text, Tool tool)
    {
        var button = MakeButton(text, (_, _) => SelectTool(tool));
        button.Tag = tool;
        return button;
    }

    private static void SetPixel(Board board, int col, int row, bool on)
    {
        if (board.Pixels[row, col] == on)
        {
            return;
        }

        board.Pixels[row, col] = on;
        board.Canvas.InvalidateVisual();
    }

    private static List<(int X, int Y)> TraceLine(int x0, int y0, int x1, int y1)
    {
        var ink = new List<(int X, int Y)>();
        var dx = Math.Abs(x1 - x0);
        var dy = Math.Abs(y1 - y0);
        var sx = x0 < x1 ? 1 : -1;
        var sy = y0 < y1 ? 1 : -1;
        var err = dx - dy;
        while (true)
        {
            ink.Add((x0, y0));
            if (x0 == x1 && y0 == y1)
            {
                break;
            }

            var e2 = err * 2;
            if (e2 > -dy)
            {
                err -= dy;
                x0 += sx;
            }

            if (e2 < dx)
            {
                err += dx;
                y0 += sy;
            }
        }

        return ink;
    }

    private static TextBox FieldBox(string text, double width) => new()
    {
        Text = text,
        Width = width,
        Margin = new Thickness(0, 0, 8, 6),
        VerticalAlignment = VerticalAlignment.Center,
        Background = Brushes.White,
        Foreground = Brushes.Black,
        CaretBrush = Brushes.Black
    };

    private static Style FontItemStyle()
    {
        var style = new Style(typeof(ComboBoxItem));
        style.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.Black));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.White));
        var favorite = new DataTrigger
        {
            Binding = new System.Windows.Data.Binding(nameof(MobitecSignFont.IsFavorite)),
            Value = true
        };
        favorite.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.Bold));
        favorite.Setters.Add(new Setter(Control.BackgroundProperty, Freeze(Color.FromRgb(0xFF, 0xF3, 0xC4))));
        style.Triggers.Add(favorite);
        return style;
    }

    private static TextBlock Label(string text) => new()
    {
        Text = text,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(8, 0, 6, 6),
        Foreground = LabelBrush()
    };

    private static Button MakeButton(string text, RoutedEventHandler onClick, bool outlined = true)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(0, 0, 8, 6),
            Cursor = Cursors.Hand,
            Foreground = outlined ? LabelBrush() : UiTheme.OnPrimaryBrush
        };
        button.Click += onClick;
        try
        {
            button.Style = (Style)Application.Current.FindResource(
                outlined ? "MaterialDesignOutlinedButton" : "MaterialDesignRaisedButton");
        }
        catch
        {
            // Fallback ohne MaterialDesign-Theme
        }

        return button;
    }

    private static Brush LabelBrush() => UiTheme.ForegroundBrush;

    private static SolidColorBrush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Pen CreateSelectionPen()
    {
        var pen = new Pen(Freeze(Color.FromRgb(0x80, 0xD8, 0xFF)), 2);
        pen.Freeze();
        return pen;
    }

    private enum Tool
    {
        Point,
        Line,
        Text,
        Eraser,
        Mark
    }

    private sealed class PlacedMark
    {
        public PlacedMark(Board board, int page, List<(int X, int Y)> ink)
        {
            Board = board;
            Page = page;
            Ink = ink;
        }

        public Board Board { get; }

        public int Page { get; }

        public List<(int X, int Y)> Ink { get; }

        public string? Text { get; set; }

        public char FontId { get; set; }
    }

    private sealed class Board
    {
        private readonly List<bool[,]> _pages;
        private readonly List<bool[,]> _originalPages;
        private readonly List<List<bool[,]>> _undos;
        private readonly IReadOnlyList<MobitecSignFont> _fonts;
        private readonly bool[,] _blank;
        private bool _blankShown;

        public Action? Changed { get; set; }

        public Board(string title, string sizeLabel, byte address, int width, byte[]? original, IReadOnlyList<MobitecSignFont> fonts)
        {
            Title = title;
            SizeLabel = sizeLabel;
            Address = address;
            Width = width;
            _fonts = fonts;
            Original = original is { Length: > 0 } ? (byte[])original.Clone() : null;
            var decoded = MobitecBitmapFrame.DecodeSign(Original, width, fonts);
            _pages = decoded.Pages.ToList();
            _originalPages = _pages.Select(MobitecBitmapFrame.Clone).ToList();
            _undos = _pages.Select(_ => new List<bool[,]>()).ToList();
            _blank = new bool[MobitecBitmapFrame.Height, width];
            IntervalSeconds = decoded.IntervalSeconds;
            Canvas = new SignCanvas(_pages[0], width);
        }

        public string Title { get; }

        public string SizeLabel { get; }

        public byte Address { get; }

        public int Width { get; }

        public byte[]? Original { get; private set; }

        public void ResetBlank()
        {
            Original = null;
            _pages.Clear();
            _pages.Add(new bool[MobitecBitmapFrame.Height, Width]);
            _originalPages.Clear();
            _originalPages.Add(MobitecBitmapFrame.Clone(_pages[0]));
            _undos.Clear();
            _undos.Add([]);
            PageSlot = 0;
            _blankShown = false;
            Canvas.Pixels = _pages[0];
            Canvas.InvalidateVisual();
            UpdateTitle(0, missing: false);
        }

        public int IntervalSeconds { get; }

        public int PageCount => _pages.Count;

        public int PageSlot { get; private set; }

        public bool IsBlankShown => _blankShown;

        public TextBlock TitleBlock { get; set; } = new();

        public bool[,] Pixels => _pages[PageSlot];

        public SignCanvas Canvas { get; }

        public List<bool[,]> Undo => _undos[PageSlot];

        public void Show(int globalIndex)
        {
            if (_pages.Count == 1 || globalIndex < _pages.Count)
            {
                ShowSlot(_pages.Count == 1 ? 0 : globalIndex);
                return;
            }

            _blankShown = true;
            Canvas.Pixels = _blank;
            Canvas.InvalidateVisual();
            UpdateTitle(globalIndex, missing: true);
        }

        /// <summary>Legt fehlende Seiten an, damit auf die gezeigte Seite gezeichnet werden kann.</summary>
        public bool Materialize(int globalIndex)
        {
            var before = _pages.Count;
            var target = Math.Clamp(globalIndex, 0, 7);
            // Eine einzelne Seite wird auf allen Wechsel-Seiten gezeigt. Der erste
            // Strich auf einer späteren Seite löst sie als eigene Kopie ab.
            var repeated = _pages.Count == 1;
            while (_pages.Count <= target)
            {
                var page = repeated
                    ? MobitecBitmapFrame.Clone(_pages[0])
                    : new bool[MobitecBitmapFrame.Height, Width];
                _pages.Add(page);
                _undos.Add([]);
            }

            ShowSlot(Math.Min(globalIndex, _pages.Count - 1));
            return _pages.Count != before;
        }

        public void AppendTowards(int index)
        {
            while (_pages.Count <= index)
            {
                var gap = _pages.Count < index;
                bool[,] copy;
                if (gap && _pages.Count == 1)
                {
                    copy = MobitecBitmapFrame.Clone(_pages[0]);
                }
                else if (!gap && !_blankShown)
                {
                    var slot = _pages.Count == 1 ? 0 : Math.Min(PageSlot, _pages.Count - 1);
                    copy = MobitecBitmapFrame.Clone(_pages[slot]);
                }
                else
                {
                    copy = new bool[MobitecBitmapFrame.Height, Width];
                }

                _pages.Add(copy);
                _undos.Add([]);
            }
        }

        public void RemoveAt(int index)
        {
            if (_pages.Count <= 1 || index < 0 || index >= _pages.Count)
            {
                return;
            }

            _pages.RemoveAt(index);
            _undos.RemoveAt(index);
            PageSlot = Math.Min(PageSlot, _pages.Count - 1);
        }

        public void PushUndo()
        {
            var stack = Undo;
            stack.Add(MobitecBitmapFrame.Clone(Pixels));
            if (stack.Count > 40)
            {
                stack.RemoveAt(0);
            }

            Changed?.Invoke();
        }

        public byte[]? ToFrame(int interval)
        {
            byte[]? frame;
            var preferText = true;
            if (Unchanged())
            {
                var keepOriginal = Original is null || _pages.Count <= 1 || interval == IntervalSeconds;
                if (!preferText || Original is null || !MobitecBitmapFrame.ContainsBitmap(Original))
                {
                    frame = keepOriginal ? Original : MobitecBitmapFrame.WithInterval(Original, interval);
                }
                else
                {
                    var asText = MobitecBitmapFrame.EncodePreferringText(_pages, Address, Width, interval, _fonts);
                    frame = asText is not null && MobitecBitmapFrame.ContainsText(asText)
                        ? asText
                        : keepOriginal
                            ? Original
                            : MobitecBitmapFrame.WithInterval(Original!, interval);
                }
            }
            else if (preferText)
            {
                frame = MobitecBitmapFrame.EncodePreferringText(_pages, Address, Width, interval, _fonts);
            }
            else
            {
                frame = MobitecBitmapFrame.EncodePages(_pages, Address, Width, interval);
            }

            return frame;
        }

        private void ShowSlot(int slot)
        {
            _blankShown = false;
            PageSlot = slot;
            Canvas.Pixels = _pages[slot];
            Canvas.InvalidateVisual();
            UpdateTitle(slot, missing: false);
        }

        private void UpdateTitle(int index, bool missing)
        {
            if (_pages.Count <= 1)
            {
                TitleBlock.Text = $"{Title}  {SizeLabel}";
                return;
            }

            TitleBlock.Text = missing
                ? $"{Title}  {SizeLabel}  · keine Seite {index + 1}"
                : $"{Title}  {SizeLabel}  · Seite {index + 1}";
        }

        private bool Unchanged()
        {
            if (_pages.Count != _originalPages.Count)
            {
                return false;
            }

            for (var i = 0; i < _pages.Count; i++)
            {
                if (!MobitecBitmapFrame.Same(_pages[i], _originalPages[i]))
                {
                    return false;
                }
            }

            return true;
        }
    }

    private sealed class SignCanvas : FrameworkElement
    {
        private readonly int _columns;

        public SignCanvas(bool[,] pixels, int columns)
        {
            Pixels = pixels;
            _columns = columns;
            Width = columns * Cell + 4;
            Height = MobitecBitmapFrame.Height * Cell + 4;
            Cursor = Cursors.Cross;
            SnapsToDevicePixels = true;
        }

        public bool[,] Pixels { get; set; }

        public bool Active { get; set; }

        public bool HasSelection { get; private set; }

        public int SelectionX { get; private set; }

        public int SelectionY { get; private set; }

        public int SelectionWidth { get; private set; }

        public int SelectionHeight { get; private set; }

        public void SetSelection(int x, int y, int width, int height)
        {
            HasSelection = width > 0 && height > 0;
            SelectionX = x;
            SelectionY = y;
            SelectionWidth = width;
            SelectionHeight = height;
            InvalidateVisual();
        }

        public void ClearSelection()
        {
            if (!HasSelection)
            {
                return;
            }

            HasSelection = false;
            InvalidateVisual();
        }

        public bool HasMarquee { get; private set; }

        public int MarqueeX { get; private set; }

        public int MarqueeY { get; private set; }

        public int MarqueeWidth { get; private set; }

        public int MarqueeHeight { get; private set; }

        public void SetMarquee(int x, int y, int width, int height)
        {
            HasMarquee = width > 0 && height > 0;
            MarqueeX = x;
            MarqueeY = y;
            MarqueeWidth = width;
            MarqueeHeight = height;
            InvalidateVisual();
        }

        public void ClearMarquee()
        {
            if (!HasMarquee)
            {
                return;
            }

            HasMarquee = false;
            InvalidateVisual();
        }

        public bool TryCell(Point position, out int column, out int row)
        {
            column = (int)((position.X - 2) / Cell);
            row = (int)((position.Y - 2) / Cell);
            return column >= 0 && row >= 0 && column < _columns && row < MobitecBitmapFrame.Height;
        }

        protected override void OnRender(DrawingContext context)
        {
            context.DrawRectangle(
                SignBlack,
                new Pen(Active ? Amber : CellOff, 2),
                new Rect(0, 0, _columns * Cell + 4, MobitecBitmapFrame.Height * Cell + 4));
            for (var row = 0; row < MobitecBitmapFrame.Height; row++)
            {
                for (var column = 0; column < _columns; column++)
                {
                    context.DrawRectangle(
                        Pixels[row, column] ? Amber : CellOff,
                        null,
                        new Rect(2 + column * Cell + 1, 2 + row * Cell + 1, Cell - 1, Cell - 1));
                }
            }

            if (HasSelection)
            {
                context.DrawRectangle(
                    SelectionFill,
                    SelectionPen,
                    new Rect(
                        2 + SelectionX * Cell,
                        2 + SelectionY * Cell,
                        SelectionWidth * Cell,
                        SelectionHeight * Cell));
            }

            if (HasMarquee)
            {
                context.DrawRectangle(
                    SelectionFill,
                    SelectionPen,
                    new Rect(
                        2 + MarqueeX * Cell,
                        2 + MarqueeY * Cell,
                        MarqueeWidth * Cell,
                        MarqueeHeight * Cell));
            }
        }
    }
}

public sealed record MobitecGraphicEdit(
    byte[]? Front,
    byte[]? Side,
    byte[]? Line,
    bool CreateNew,
    string Name,
    string LineText,
    int? DestinationNumber,
    int IntervalSeconds);
