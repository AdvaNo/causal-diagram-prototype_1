using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Newtonsoft.Json;

namespace CausalDiagram_1
{
    public class MainForm : Form
    {
        private StatusStrip _statusStrip;
        private ToolStripStatusLabel _statusLabel;
        //отладка

        private Diagram _diagram = new Diagram();
        private readonly CommandManager _cmd = new CommandManager();

        private List<ForbiddenRule> _forbiddenRules = new List<ForbiddenRule>();
        private bool _enforceForbiddenLinks = true; // если true — запрещённые связи блокируются (показываем диалог)

        // Размеры узла (прямоугольник)
        private const int NodeWidth = 120;
        private const int NodeHeight = 60;

        // Текущий цвет для создания новых узлов
        private NodeColor _currentColor = NodeColor.Green;

        // Кнопка "Выбрать" (мышка) - объявите поле чтобы можно было подсвечивать
        private Button _btnSelect;

        // UI
        private Panel _canvas;
        private ToolStrip _tool;
        private PropertyGrid _propGrid;
        private Button _btnAddNode, _btnConnect, _btnDelete, _btnUndo, _btnRedo, _btnPaint, _btnTraceCause  /*_btnFmea*/;
        private string _currentFile;

        // Interaction state
        private enum Mode { Select, AddNode, Connect, Pan, Paint }
        private Mode _mode = Mode.Select;
        private Node _dragNode = null;
        private PointF _dragStart;
        private bool _isPanning = false;
        private float _scale = 1.0f;
        private PointF _panOffset = new PointF(0, 0);
        private Node _connectFrom = null;

        private const int NodeRadius = 40;

        private Node _propGridOldSnapshot = null;

        // выбранное ребро (если есть)
        private Guid _selectedEdgeId = Guid.Empty;
        // порог для hit-test по ребру (в пикселях, в координатах canvas)
        private const float EdgeHitTestThreshold = 8f;

        //меню при узле
        private ContextMenuStrip _canvasContext;

        // --- Grid ---
        private bool _showGrid = false;
        private int _gridStep = 20; // пиксели
        private bool _snapToGrid = true; // при создании/перемещении привязываем к сетке

        // --- Copy/Paste ---
        private const string CLIPBOARD_FORMAT = "CausalDiagramSubgraph_v1"; // необязательный, используем plain text JSON
                                                                            // для простоты используем Clipboard.SetText/GetText с JSON
                                                                            // Multi-select fields
        private HashSet<Guid> _selectedNodeIds = new HashSet<Guid>();
        private bool _isSelectingRect = false;
        private Point _selectionStartScreen;    // в экранных (control) координатах
        private Rectangle _selectionRectScreen; // в экранных координатах, для рисования

        // --- Autosave ---
        private System.Windows.Forms.Timer _autosaveTimer;
        private string _autosavePath;
        private int _autosaveIntervalMs = 30000; // 30 секунд


        public MainForm()
        {
            Text = "Автомат — причинно-следственная диаграмма (прототип)";
            Width = 1200;
            Height = 800;

            InitializeUi();

            try
            {
                _autosavePath = Path.Combine(Path.GetTempPath(), "causal_diagram_autosave.json");
                if (File.Exists(_autosavePath))
                {
                    var res = MessageBox.Show("Найдено автосохранение. Восстановить?", "Восстановление", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (res == DialogResult.Yes)
                    {
                        var txt = File.ReadAllText(_autosavePath);
                        var loaded = JsonConvert.DeserializeObject<Diagram>(txt);
                        if (loaded != null)
                        {
                            _diagram = loaded;
                            _cmd.Clear(); // очистим стек команд после восстановления
                            InvalidateCanvas();
                            if (_statusLabel != null) _statusLabel.Text = "Восстановлено автосохранение";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // не допускаем падения формы из-за ошибки восстановления
                MessageBox.Show("Ошибка при восстановлении автосохранения: " + ex.Message, "Восстановление", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void InitializeUi()
        {
            // ToolStrip
            _tool = new ToolStrip();
            var tsiNew = new ToolStripButton("Новый");
            var tsiOpen = new ToolStripButton("Открыть");
            var tsiSave = new ToolStripButton("Сохранить");
            var tsiExport = new ToolStripButton("Экспорт PNG");

            tsiNew.Click += (s, e) => NewDiagram();
            tsiOpen.Click += (s, e) => OpenDiagram();
            tsiSave.Click += (s, e) => SaveDiagram();
            tsiExport.Click += (s, e) => ExportPng();

            // создаём управляющие кнопки (включая Select и цветовые кнопки)
            _btnSelect = new Button { Text = "Выбрать" };
            _btnAddNode = new Button { Text = "Добавить узел" };
            _btnConnect = new Button { Text = "Соединить" };
            _btnDelete = new Button { Text = "Удалить" };
            _btnUndo = new Button { Text = "Отменить" };
            _btnRedo = new Button { Text = "Вернуть" };
            _btnTraceCause = new Button { Text = "Найти причину" };
            //_btnFmea = new Button { Text = "FMEA (RPN)" };

            // события
            _btnSelect.Click += (s, e) => SetMode(Mode.Select);
            _btnAddNode.Click += (s, e) => SetMode(Mode.AddNode);
            _btnConnect.Click += (s, e) => SetMode(Mode.Connect);
            _btnDelete.Click += (s, e) => DeleteSelected();
            _btnUndo.Click += (s, e) => { _cmd.Undo(); InvalidateCanvas(); };
            _btnRedo.Click += (s, e) => { _cmd.Redo(); InvalidateCanvas(); };
            //_btnFmea.Click += (s, e) => ShowFmeaForm();
            _btnPaint = new Button { Text = "Покрасить" };
            _btnPaint.Click += (s, e) =>
            {
                // переключаем режим Paint при нажатии
                SetMode(Mode.Paint);
            };
            _btnTraceCause.Click += (s, e) => DoTraceCause();

            // Цветовые кнопки (ToolStrip кнопки удобнее, но мы используем обычные Button-hosts)
            var btnColorGreen = new Button { Text = "Зелёный" };
            var btnColorYellow = new Button { Text = "Жёлтый" };
            var btnColorRed = new Button { Text = "Красный" };
            var btnGrid = new Button { Text = "Сетка" };
            var btnCopy = new Button { Text = "Копировать" };
            var btnPaste = new Button { Text = "Вставить" };

            var btnRules = new Button { Text = "Правила связей" };
            var btnEnforce = new Button { Text = "Enforce: ON" };
            btnRules.Click += (s, e) => EditForbiddenRules();
            btnEnforce.Click += (s, e) =>
            {
                _enforceForbiddenLinks = !_enforceForbiddenLinks;
                btnEnforce.Text = _enforceForbiddenLinks ? "Enforce: ON" : "Enforce: OFF";
            };


            btnGrid.Click += (s, e) =>
            {
                _showGrid = !_showGrid;
                btnGrid.BackColor = _showGrid ? Color.LightGreen : SystemColors.Control;
                InvalidateCanvas();
            };
            btnCopy.Click += (s, e) => CopySelected();
            btnPaste.Click += (s, e) => PasteFromClipboard();
            btnColorGreen.Click += (s, e) => { _currentColor = NodeColor.Green; UpdateColorButtons(btnColorGreen, btnColorYellow, btnColorRed); };
            btnColorYellow.Click += (s, e) => { _currentColor = NodeColor.Yellow; UpdateColorButtons(btnColorGreen, btnColorYellow, btnColorRed); };
            btnColorRed.Click += (s, e) => { _currentColor = NodeColor.Red; UpdateColorButtons(btnColorGreen, btnColorYellow, btnColorRed); };

            // hosts
            var hostSelect = new ToolStripControlHost(_btnSelect);
            var hostAdd = new ToolStripControlHost(_btnAddNode);
            var hostConnect = new ToolStripControlHost(_btnConnect);
            var hostDelete = new ToolStripControlHost(_btnDelete);
            var hostUndo = new ToolStripControlHost(_btnUndo);
            var hostRedo = new ToolStripControlHost(_btnRedo);
            //var hostFmea = new ToolStripControlHost(_btnFmea);
            var hostPaint = new ToolStripControlHost(_btnPaint);
            var hostGrid = new ToolStripControlHost(btnGrid);
            var hostCopy = new ToolStripControlHost(btnCopy);
            var hostPaste = new ToolStripControlHost(btnPaste);

            var hostColorG = new ToolStripControlHost(btnColorGreen);
            var hostColorY = new ToolStripControlHost(btnColorYellow);
            var hostColorR = new ToolStripControlHost(btnColorRed);

            var hostRules = new ToolStripControlHost(btnRules);
            var hostEnforce = new ToolStripControlHost(btnEnforce);

            // добавление в тулбар (порядок можно менять)
            _tool.Items.Add(tsiNew);
            _tool.Items.Add(tsiOpen);
            _tool.Items.Add(tsiSave);
            _tool.Items.Add(tsiExport);
            _tool.Items.Add(new ToolStripSeparator());

            _tool.Items.Add(hostSelect);
            _tool.Items.Add(hostAdd);
            _tool.Items.Add(hostConnect);
            _tool.Items.Add(hostDelete);
            _tool.Items.Add(hostUndo);
            _tool.Items.Add(hostRedo);
            //_tool.Items.Add(hostFmea);
            _tool.Items.Add(hostPaint);
            _tool.Items.Add(new ToolStripControlHost(_btnTraceCause));

            _tool.Items.Add(new ToolStripSeparator());
            _tool.Items.Add(new ToolStripLabel("Цвет:"));
            _tool.Items.Add(hostColorG);
            _tool.Items.Add(hostColorY);
            _tool.Items.Add(hostColorR);

            _tool.Items.Add(new ToolStripSeparator());
            _tool.Items.Add(hostGrid);
            _tool.Items.Add(hostCopy);
            _tool.Items.Add(hostPaste);

            _tool.Items.Add(hostRules);
            _tool.Items.Add(hostEnforce);

            //клавиша delete
            this.KeyPreview = true;
            this.KeyDown += MainForm_KeyDown;

            _canvasContext = new ContextMenuStrip();
            _canvasContext.Items.Add("Удалить связь", null, (s, e) =>
            {
                if (_selectedEdgeId != Guid.Empty)
                {
                    var edge = _diagram.Edges.FirstOrDefault(x => x.Id == _selectedEdgeId);
                    if (edge != null)
                    {
                        var cmd = new RemoveEdgeCommand(_diagram, edge);
                        _cmd.ExecuteCommand(cmd);
                        _selectedEdgeId = Guid.Empty;
                        InvalidateCanvas();
                    }
                }
            });


            // установить визуальную подсветку цвета по-умолчанию
            UpdateColorButtons(btnColorGreen, btnColorYellow, btnColorRed);

            // --- PropertyGrid (справа) ---
            _propGrid = new PropertyGrid { Dock = DockStyle.Right, Width = 300 };
            // чтобы изменения в PropertyGrid сразу перерисовывали холст
            _propGrid.PropertyValueChanged += (s, e) => {
                // SelectedObject должен быть NodeProxy
                if (_propGrid.SelectedObject is NodeProxy proxy)
                {
                    var node = proxy.Node;
                    var newSnapshot = CloneNode(node);

                    if (_propGridOldSnapshot != null)
                    {
                        var cmd = new EditNodePropertiesCommand(node, _propGridOldSnapshot, newSnapshot);
                        _cmd.ExecuteCommand(cmd);
                        _propGridOldSnapshot = CloneNode(node); // обновляем запас на следующую правку
                    }
                    else
                    {
                        // если по каким-то причинам нет старого снапшота — всё равно обновим
                        _propGridOldSnapshot = CloneNode(node);
                    }

                    InvalidateCanvas();
                }
            };

            // --- Canvas (центр) ---
            _canvas = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
            _canvas.Paint += Canvas_Paint;
            _canvas.MouseDown += Canvas_MouseDown;
            _canvas.MouseUp += Canvas_MouseUp;
            _canvas.MouseMove += Canvas_MouseMove;
            _canvas.MouseWheel += Canvas_MouseWheel;
            _canvas.Resize += (s, e) => InvalidateCanvas();
            // включаем double buffering (метод из расширения)
            this.DoubleBufferedControl(_canvas, true);

            // --- StatusStrip (внизу) ---
            _statusStrip = new StatusStrip();
            _statusLabel = new ToolStripStatusLabel("Готово");
            _statusStrip.Items.Add(_statusLabel);

            // --- Добавляем элементы управления на форму в правильном порядке ---
            Controls.Add(_canvas);       // центр
            Controls.Add(_propGrid);    // справа
            Controls.Add(_tool);        // сверху
            Controls.Add(_statusStrip); // снизу

            // Autosave initialization
            _autosavePath = Path.Combine(Path.GetTempPath(), "causal_diagram_autosave.json");
            _autosaveTimer = new System.Windows.Forms.Timer();
            _autosaveTimer.Interval = _autosaveIntervalMs;
            _autosaveTimer.Tick += (s, e) =>
            {
                try
                {
                    var json = JsonConvert.SerializeObject(_diagram, Formatting.Indented);
                    File.WriteAllText(_autosavePath, json);
                    // можно обновлять статус
                    _statusLabel.Text = "Автосохранение " + DateTime.Now.ToString("T");
                }
                catch { /* молча */ }
            };
            _autosaveTimer.Start();

            // --- Autosave initialization (добавьте в конец InitializeUi) ---
            _autosavePath = Path.Combine(Path.GetTempPath(), "causal_diagram_autosave.json");
            _autosaveTimer = new System.Windows.Forms.Timer();
            _autosaveTimer.Interval = _autosaveIntervalMs;
            _autosaveTimer.Tick += (s, e) =>
            {
                try
                {
                    var json = JsonConvert.SerializeObject(_diagram, Formatting.Indented);
                    File.WriteAllText(_autosavePath, json);
                    // optionally update status
                    if (_statusLabel != null) _statusLabel.Text = "Автосохранение " + DateTime.Now.ToString("T");
                }
                catch
                {
                    // можно логировать ошибку, но молча продолжим
                }
            };
            _autosaveTimer.Start();

            // подписываемся на закрытие формы, чтобы почистить автосохранение
            this.FormClosing += (s, e) =>
            {
                try
                {
                    _autosaveTimer?.Stop();
                    if (File.Exists(_autosavePath))
                    {
                        File.Delete(_autosavePath);
                    }
                }
                catch
                {
                    // молча
                }
            };

            // Установим режим по умолчанию
            _mode = Mode.Select;

            // Обновим статус (и перерисовку)
            UpdateStatus();
            UpdateStatusText();
            InvalidateCanvas();
        }



        private void UpdateColorButtons(Button g, Button y, Button r)
        {
            // простая визуальная подсветка: выбранная кнопка — светло-зелёная, прочие — стандарт
            g.BackColor = (_currentColor == NodeColor.Green) ? Color.LightGreen : SystemColors.Control;
            y.BackColor = (_currentColor == NodeColor.Yellow) ? Color.LightYellow : SystemColors.Control;
            r.BackColor = (_currentColor == NodeColor.Red) ? Color.LightCoral : SystemColors.Control;
        }
        private void UpdateStatusText()
        {
            try
            {
                _statusLabel.Text = $"Режим: {_mode} | Узлы: {_diagram?.Nodes?.Count ?? 0}";
            }
            catch { _statusLabel.Text = "Режим: ?"; }
        }


        private void SetMode(Mode m)
        {
            // очищаем старые подсветки при смене режима
            ClearHighlights();

            _mode = m;
            _connectFrom = null;
            UpdateStatus();
        }

        private void UpdateStatus()
        {
            _btnAddNode.BackColor = _mode == Mode.AddNode ? Color.LightGreen : SystemColors.Control;
            _btnConnect.BackColor = _mode == Mode.Connect ? Color.LightGreen : SystemColors.Control;
            _btnSelect.BackColor = (_mode == Mode.Select) ? Color.LightGreen : SystemColors.Control;
            _btnPaint.BackColor = (_mode == Mode.Paint) ? Color.LightGreen : SystemColors.Control;
        }

        private void MainForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.C)
            {
                CopySelected();
                e.Handled = true;
                return;
            }
            if (e.Control && e.KeyCode == Keys.V)
            {
                PasteFromClipboard();
                e.Handled = true;
                return;
            }
            if (e.KeyCode == Keys.Delete)
            {
                DeleteSelected();
                e.Handled = true;
            }
        }


        #region Canvas drawing & helpers

        private void Canvas_Paint(object sender, PaintEventArgs e)
        {
            try
            {
                var g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                // apply transform: scale then translate (pan)
                var m = g.Transform;
                m.Reset();
                m.Scale(_scale, _scale);
                m.Translate(_panOffset.X, _panOffset.Y);
                g.Transform = m;

                // draw grid (если включена)
                if (_showGrid)
                {
                    var clientRect = _canvas.ClientRectangle;
                    var topLeft = ScreenToCanvas(new Point(0, 0));
                    var bottomRight = ScreenToCanvas(new Point(_canvas.Width, _canvas.Height));

                    float x0 = (float)Math.Floor(topLeft.X / _gridStep) * _gridStep;
                    float x1 = (float)Math.Ceiling(bottomRight.X / _gridStep) * _gridStep;
                    float y0 = (float)Math.Floor(topLeft.Y / _gridStep) * _gridStep;
                    float y1 = (float)Math.Ceiling(bottomRight.Y / _gridStep) * _gridStep;

                    using (var pen = new Pen(Color.FromArgb(120, Color.LightGray), 1f))
                    {
                        pen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dot;
                        for (float gx = x0; gx <= x1; gx += _gridStep)
                            g.DrawLine(pen, gx, y0, gx, y1);
                        for (float gy = y0; gy <= y1; gy += _gridStep)
                            g.DrawLine(pen, x0, gy, x1, gy);
                    }
                }

                // draw edges
                foreach (var edge in _diagram.Edges)
                {
                    var from = _diagram.Nodes.FirstOrDefault(n => n.Id == edge.From);
                    var to = _diagram.Nodes.FirstOrDefault(n => n.Id == edge.To);
                    if (from == null || to == null) continue;

                    bool isSelOrHighlight = (edge.Id == _selectedEdgeId) || edge.IsHighlighted;
                    bool isForbidden = edge.IsForbidden;
                    DrawArrow(g, new PointF(from.X, from.Y), new PointF(to.X, to.Y), isSelOrHighlight, isForbidden);
                }

                // draw nodes
                foreach (var node in _diagram.Nodes)
                {
                    DrawNode(g, node);
                }

                // рисуем rubber-band в экранных координатах поверх всего — ВНУТРИ try (г доступна)
                if (_isSelectingRect)
                {
                    // сбросим transform чтобы нарисовать в pixel-координатах контролла
                    var prevTransform = g.Transform;
                    g.ResetTransform();

                    // рисуем пунктирную рамку
                    using (var pen = new Pen(Color.FromArgb(180, Color.Blue), 1f))
                    {
                        pen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;
                        g.DrawRectangle(pen, _selectionRectScreen);
                        // слегка заполнить полупрозрачным цветом
                        using (var brush = new SolidBrush(Color.FromArgb(40, Color.LightBlue)))
                        {
                            g.FillRectangle(brush, _selectionRectScreen);
                        }
                    }

                    // восстановим трансформ
                    g.Transform = prevTransform;
                }
            }
            catch (Exception ex)
            {
                // безопасный лог и уведомление
                try
                {
                    string tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "causal_diagram_error.log");
                    System.IO.File.AppendAllText(tmp, DateTime.Now.ToString("s") + " — Canvas_Paint exception:\r\n" + ex.ToString() + "\r\n\r\n");
                }
                catch { /* негромкая неудача логирования */ }

                MessageBox.Show("Ошибка при отрисовке холста: " + ex.Message + "\nСм. лог: causal_diagram_error.log в %TEMP%", "Ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void DrawNode(Graphics g, Node node)
        {
            var center = new PointF(node.X, node.Y);
            var rect = new RectangleF(center.X - NodeWidth / 2f, center.Y - NodeHeight / 2f, NodeWidth, NodeHeight);

            // выбор заливки и формы (острые/закруглённые)
            Brush fill;
            bool rounded = false;
            switch (node.ColorName)
            {
                case NodeColor.Green:
                    fill = Brushes.LightGreen;
                    rounded = false;
                    break;
                case NodeColor.Yellow:
                    fill = Brushes.LightYellow;
                    rounded = false;
                    break;
                case NodeColor.Red:
                    fill = Brushes.LightCoral;
                    rounded = true;
                    break;
                default:
                    fill = Brushes.LightGray;
                    rounded = false;
                    break;
            }

            // рисуем базовую форму
            if (rounded)
            {
                using (var path = RoundedRectPath(rect, 12f))
                {
                    g.FillPath(fill, path);
                    using (var borderPen = new Pen(Color.DarkRed, 1f))
                        g.DrawPath(borderPen, path);
                }
            }
            else
            {
                g.FillRectangle(fill, rect);
                using (var borderPen = new Pen(Color.Black, 1f))
                    g.DrawRectangle(borderPen, rect.X, rect.Y, rect.Width, rect.Height);
            }

            // Trace highlight (исходящий от DoTraceCause) — оранжево-красная рамка
            if (node.IsHighlighted)
            {
                using (var hlPen = new Pen(Color.OrangeRed, 3f))
                {
                    hlPen.DashStyle = System.Drawing.Drawing2D.DashStyle.Solid;
                    if (rounded)
                    {
                        using (var path = RoundedRectPath(rect, 12f))
                            g.DrawPath(hlPen, path);
                    }
                    else
                    {
                        g.DrawRectangle(hlPen, rect.X, rect.Y, rect.Width, rect.Height);
                    }
                }
            }

            // Selection (пользователь выбрал узел) — синяя толстая рамка поверх всего
            bool isSelected = _selectedNodeIds.Contains(node.Id)
                              || (_propGrid?.SelectedObject is NodeProxy np && np.Node.Id == node.Id);
            if (isSelected)
            {
                using (var selPen = new Pen(Color.DodgerBlue, 3.5f))
                {
                    selPen.DashStyle = System.Drawing.Drawing2D.DashStyle.Solid;
                    if (rounded)
                    {
                        using (var path = RoundedRectPath(rect, 12f))
                            g.DrawPath(selPen, path);
                    }
                    else
                    {
                        g.DrawRectangle(selPen, rect.X, rect.Y, rect.Width, rect.Height);
                    }
                }
            }

            // Текст: многострочный, центрированный, auto-fit (уменьшение шрифта при необходимости)
            var text = string.IsNullOrEmpty(node.Title) ? "(без названия)" : node.Title;
            var sf = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisWord,
                FormatFlags = StringFormatFlags.LineLimit
            };

            var layoutRect = RectangleF.Inflate(rect, -6f, -6f); // внутренний отступ

            // Автоуменьшение шрифта, чтобы текст поместился
            float fontSize = 10f;
            float minFont = 6f;
            var baseFamily = SystemFonts.DefaultFont.FontFamily;

            Font font = new Font(baseFamily, fontSize);
            SizeF measured = g.MeasureString(text, font, (int)layoutRect.Width, sf);


            // измерение с временным шрифтом, уменьшение до подходящего размера
            using (var testFont = new Font(baseFamily, fontSize))
            {
                //var measured = g.MeasureString(text, testFont, (int)layoutRect.Width, sf);
                float current = fontSize;
                while ((measured.Width > layoutRect.Width || measured.Height > layoutRect.Height) && current > minFont)
                {
                    current -= 0.5f;
                    measured = g.MeasureString(text, new Font(baseFamily, current), (int)layoutRect.Width, sf);
                }

                using (var finalFont = new Font(baseFamily, Math.Max(current, minFont)))
                {
                    g.DrawString(text, finalFont, Brushes.Black, layoutRect, sf);
                }
            }

            // Проверим вместимость и уменьшаем при необходимости
            while ((measured.Height > layoutRect.Height || measured.Width > layoutRect.Width) && fontSize > 6f)
            {
                fontSize -= 0.5f;
                font.Dispose();
                font = new Font(baseFamily, fontSize);
                measured = g.MeasureString(text, font, (int)layoutRect.Width, sf);
            }

            g.DrawString(text, font, Brushes.Black, layoutRect, sf);
            font.Dispose();
        }

        private GraphicsPath RoundedRectPath(RectangleF rect, float radius)
        {
            var path = new GraphicsPath();
            float r = Math.Max(0, radius);
            float diameter = r * 2f;

            // верхняя левая дуга
            path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
            // верхняя правая дуга
            path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
            // нижняя правая дуга
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            // нижняя левая дуга
            path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
        //private System.Drawing.Drawing2D.GraphicsPath RoundedRectPath(RectangleF rect, float radius)
        //{
        //    var path = new System.Drawing.Drawing2D.GraphicsPath();
        //    float diameter = radius * 2f;
        //    var arcRect = new RectangleF(rect.Location, new SizeF(diameter, diameter));

        //    // top-left
        //    path.AddArc(arcRect, 180, 90);

        //    // top-right
        //    arcRect.X = rect.Right - diameter;
        //    path.AddArc(arcRect, 270, 90);

        //    // bottom-right
        //    arcRect.Y = rect.Bottom - diameter;
        //    path.AddArc(arcRect, 0, 90);

        //    // bottom-left
        //    arcRect.X = rect.Left;
        //    path.AddArc(arcRect, 90, 90);

        //    path.CloseFigure();
        //    return path;
        //}


        private void DrawArrow(Graphics g, PointF pFrom, PointF pTo, bool highlight = false, bool forbidden = false)
        {
            // Толщина и цвет зависят от состояния (highlight/selected)
            float penWidth = forbidden ? 2.5f : (highlight ? 3f : 2f);
            Color penColor = forbidden ? Color.Red : (highlight ? Color.OrangeRed : Color.DarkGreen);

            using (var pen = new Pen(penColor, penWidth))
            {
                // делаем начало линии более аккуратным при большой толщине
                pen.EndCap = System.Drawing.Drawing2D.LineCap.Flat;
                pen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                pen.LineJoin = System.Drawing.Drawing2D.LineJoin.Round;
                if (forbidden) pen.DashStyle = DashStyle.Dash;

                // вычисляем bounding rect для узлов (как у вас)
                var fromRect = new RectangleF(pFrom.X - NodeWidth / 2f, pFrom.Y - NodeHeight / 2f, NodeWidth, NodeHeight);
                var toRect = new RectangleF(pTo.X - NodeWidth / 2f, pTo.Y - NodeHeight / 2f, NodeWidth, NodeHeight);

                // точки на границах прямоугольников (ваша функция должна учитывать форму)
                var pt1 = GetRectBoundaryPointTowards(fromRect, pTo);
                var pt2 = GetRectBoundaryPointTowards(toRect, pFrom);

                // основная линия
                g.DrawLine(pen, pt1, pt2);

                // стрелочный наконечник (треугольник) в конце pt2
                var dir = new PointF(pt2.X - pt1.X, pt2.Y - pt1.Y);
                float len = (float)Math.Sqrt(dir.X * dir.X + dir.Y * dir.Y);
                if (len < 0.0001f) return;
                var ux = dir.X / len;
                var uy = dir.Y / len;

                // размер стрелки подстраиваем под толщину линии
                float arrowLen = 12f + penWidth * 1.5f;
                float halfWidth = 6f + penWidth;

                // перпендикуляр к направлению
                var px = -uy;
                var py = ux;

                var a = new PointF(pt2.X, pt2.Y);
                var b = new PointF(pt2.X - ux * arrowLen + px * halfWidth, pt2.Y - uy * arrowLen + py * halfWidth);
                var c = new PointF(pt2.X - ux * arrowLen - px * halfWidth, pt2.Y - uy * arrowLen - py * halfWidth);

                using (var brush = new SolidBrush(pen.Color))
                {
                    g.FillPolygon(brush, new[] { a, b, c });
                }
            }
        }



        // Возвращает точку на границе rect в направлении к target
        private PointF GetRectBoundaryPointTowards(RectangleF rect, PointF target)
        {
            var center = new PointF(rect.X + rect.Width / 2f, rect.Y + rect.Height / 2f);
            var dx = target.X - center.X;
            var dy = target.Y - center.Y;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 0.0001f) return center;

            var ux = dx / len;
            var uy = dy / len;

            // расстояние до границы по x и y
            float hx = rect.Width / 2f;
            float hy = rect.Height / 2f;

            float tx = Math.Abs(hx / ux);
            float ty = Math.Abs(hy / uy);

            float t = Math.Min(tx, ty);

            // вернём точку на границе
            return new PointF(center.X + ux * t, center.Y + uy * t);
        }


        private Node HitTestNode(PointF canvasPoint)
        {
            // проверяем, попадает ли точка в прямоугольник узла
            return _diagram.Nodes.FirstOrDefault(n =>
                Math.Abs(canvasPoint.X - n.X) <= NodeWidth / 2f &&
                Math.Abs(canvasPoint.Y - n.Y) <= NodeHeight / 2f);
        }

        private static float Distance(float x1, float y1, float x2, float y2)
        {
            var dx = x1 - x2;
            var dy = y1 - y2;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        // проверяет, попадает ли canvasPoint близко к какой-либо линии-ребру; возвращает Edge или null
        private Edge HitTestEdge(PointF canvasPoint)
        {
            foreach (var edge in _diagram.Edges)
            {
                var from = _diagram.Nodes.FirstOrDefault(n => n.Id == edge.From);
                var to = _diagram.Nodes.FirstOrDefault(n => n.Id == edge.To);
                if (from == null || to == null) continue;

                var p1 = GetRectBoundaryPointTowards(new RectangleF(from.X - NodeWidth / 2f, from.Y - NodeHeight / 2f, NodeWidth, NodeHeight), new PointF(to.X, to.Y));
                var p2 = GetRectBoundaryPointTowards(new RectangleF(to.X - NodeWidth / 2f, to.Y - NodeHeight / 2f, NodeWidth, NodeHeight), new PointF(from.X, from.Y));

                float d = DistancePointToSegment(canvasPoint, p1, p2);
                if (d <= EdgeHitTestThreshold) return edge;
            }
            return null;
        }

        // helper: расстояние от точки p до отрезка a-b
        private float DistancePointToSegment(PointF p, PointF a, PointF b)
        {
            float vx = b.X - a.X;
            float vy = b.Y - a.Y;
            float wx = p.X - a.X;
            float wy = p.Y - a.Y;
            float c1 = vx * wx + vy * wy;
            if (c1 <= 0) return Distance(p.X, p.Y, a.X, a.Y);
            float c2 = vx * vx + vy * vy;
            if (c2 <= c1) return Distance(p.X, p.Y, b.X, b.Y);
            float t = c1 / c2;
            float projX = a.X + t * vx;
            float projY = a.Y + t * vy;
            return Distance(p.X, p.Y, projX, projY);
        }


        private void InvalidateCanvas() => _canvas.Invalidate();

        #endregion

        #region Mouse handling (add/move/connect/pan/zoom)

        private PointF ScreenToCanvas(Point screenPt)
        {
            return new PointF((screenPt.X / _scale) - _panOffset.X, (screenPt.Y / _scale) - _panOffset.Y);
        }

        private void Canvas_MouseDown(object sender, MouseEventArgs e)
        {
            _canvas.Focus();
            var p = ScreenToCanvas(e.Location);

            if (_mode == Mode.Paint && e.Button == MouseButtons.Left)
            {
                var node = HitTestNode(p);
                if (node != null)
                {
                    var cmd = new ChangeNodeColorCommand(node, _currentColor);
                    _cmd.ExecuteCommand(cmd);
                    InvalidateCanvas();
                }
                return; // выбрать цвет - покрасить - по узлу
            }

            if (e.Button == MouseButtons.Middle)
            {
                _isPanning = true;
                _dragStart = e.Location;
                return;
            }

            if (_mode == Mode.AddNode && e.Button == MouseButtons.Left)
            {
                float nx = p.X;
                float ny = p.Y;
                if (_snapToGrid && _gridStep > 0)
                {
                    nx = (float)Math.Round(nx / _gridStep) * _gridStep;
                    ny = (float)Math.Round(ny / _gridStep) * _gridStep;
                }
                var node = new Node { X = nx, Y = ny, Title = "Новый узел", ColorName = _currentColor };
                var cmd = new AddNodeCommand(_diagram, node);
                _cmd.ExecuteCommand(cmd);
                InvalidateCanvas();
                return;
            }

            if (_mode == Mode.Connect && e.Button == MouseButtons.Left)
            {
                var node = HitTestNode(p);
                if (node != null)
                {
                    if (_connectFrom == null)
                    {
                        _connectFrom = node;
                        return;
                    }
                    else if (_connectFrom != node)
                    {
                        // проверка на запрещённые связи
                        var fromNode = _connectFrom;
                        var toNode = node;
                        ForbiddenRule violated;
                        if (_enforceForbiddenLinks && IsForbiddenConnection(fromNode, toNode, out violated))
                        {
                            var msg = $"Создание связи {fromNode.Title} → {toNode.Title} запрещено правилом: {violated}\nДобавить всё равно?";
                            var res = MessageBox.Show(msg, "Запрещённая связь", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                            if (res == DialogResult.No)
                            {
                                _connectFrom = null;
                                return;
                            }
                        }

                        var edge = new Edge { Id = Guid.NewGuid(), From = _connectFrom.Id, To = node.Id };
                        if (IsForbiddenConnection(fromNode, toNode, out violated))
                            edge.IsForbidden = true;

                        var cmd = new AddEdgeCommand(_diagram, edge);
                        _cmd.ExecuteCommand(cmd);
                        _connectFrom = null;
                        InvalidateCanvas();
                        return;
                    }
                }
            }

            // В режиме Select: выбор и/или перетаскивание узла и ребёр
            if (_mode == Mode.Select && e.Button == MouseButtons.Left)
            {
                // используем уже вычисленную выше переменную p

                // сначала проверим, не попал ли пользователь по ребру
                var hitEdge = HitTestEdge(p);
                if (hitEdge != null)
                {
                    _selectedEdgeId = hitEdge.Id;
                    // снимем выделение узла (если нужно)
                    _propGrid.SelectedObject = null;
                    _dragNode = null;
                    _selectedNodeIds.Clear();
                    InvalidateCanvas();
                    return;
                }

                // просто выбор узла
                var clickedNode = HitTestNode(p);
                if (clickedNode != null)
                {
                    bool ctrl = (ModifierKeys & Keys.Control) == Keys.Control;
                    bool shift = (ModifierKeys & Keys.Shift) == Keys.Shift;

                    if (ctrl || shift)
                    {
                        // инвертируем включение в выборку
                        if (_selectedNodeIds.Contains(clickedNode.Id))
                            _selectedNodeIds.Remove(clickedNode.Id);
                        else
                            _selectedNodeIds.Add(clickedNode.Id);

                        // обновим PropertyGrid: если 1 элемент выбран — показываем его, иначе очищаем
                        UpdatePropertyGridSelection();
                        InvalidateCanvas();
                        return;
                    }
                    else
                    {
                        // обычный клик — один выбранный узел (очищаем предыдущую выборку)
                        _selectedNodeIds.Clear();
                        _selectedNodeIds.Add(clickedNode.Id);

                        // начать drag перемещение узла как раньше
                        _dragNode = clickedNode;
                        _dragStart = new PointF(clickedNode.X, clickedNode.Y);

                        UpdatePropertyGridSelection();
                        InvalidateCanvas();
                        return;
                    }
                }

                // Клик в пустой области — начинаем rubber-band selection
                _isSelectingRect = true;
                _selectionStartScreen = e.Location;
                _selectionRectScreen = new Rectangle(e.Location, Size.Empty);
                // если без модификаторов — очистить существующий выбор, иначе начать добавление
                bool ctrlOrShift = (ModifierKeys & (Keys.Control | Keys.Shift)) != 0;
                if (!ctrlOrShift)
                    _selectedNodeIds.Clear();
                // снимем выделение ребра
                _selectedEdgeId = Guid.Empty;
                _propGrid.SelectedObject = null;
                _dragNode = null;
                InvalidateCanvas();
                return;
            }
        }

        private Node CloneNode(Node n)
        {
            return new Node
            {
                Id = n.Id,
                Title = n.Title,
                Description = n.Description,
                X = n.X,
                Y = n.Y,
                Weight = n.Weight,
                ColorName = n.ColorName,
                Severity = n.Severity,
                Occurrence = n.Occurrence,
                Detectability = n.Detectability
            };
        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            var p = ScreenToCanvas(e.Location);

            if (_isPanning && e.Button == MouseButtons.Middle)
            {
                var delta = new PointF((e.Location.X - _dragStart.X) / _scale, (e.Location.Y - _dragStart.Y) / _scale);
                _panOffset.X += delta.X;
                _panOffset.Y += delta.Y;
                _dragStart = e.Location;
                InvalidateCanvas();
                return;
            }

            if (_dragNode != null && e.Button == MouseButtons.Left)
            {
                var pe = ScreenToCanvas(e.Location);
                _dragNode.X = pe.X;
                _dragNode.Y = pe.Y;
                InvalidateCanvas();
                return;
            }

            if (_isSelectingRect)
            {
                var x = Math.Min(_selectionStartScreen.X, e.Location.X);
                var y = Math.Min(_selectionStartScreen.Y, e.Location.Y);
                var w = Math.Abs(_selectionStartScreen.X - e.Location.X);
                var h = Math.Abs(_selectionStartScreen.Y - e.Location.Y);
                _selectionRectScreen = new Rectangle(x, y, w, h);
                InvalidateCanvas();
                return;
            }
        }

        private void Canvas_MouseUp(object sender, MouseEventArgs e)
        {
            var p = ScreenToCanvas(e.Location);

            if (_isPanning && e.Button == MouseButtons.Middle)
            {
                _isPanning = false;
                return;
            }

            if (_dragNode != null && e.Button == MouseButtons.Left)
            {
                var newX = _dragNode.X;
                var newY = _dragNode.Y;
                if (_snapToGrid && _gridStep > 0)
                {
                    newX = (float)Math.Round(newX / _gridStep) * _gridStep;
                    newY = (float)Math.Round(newY / _gridStep) * _gridStep;
                    // применим скорректированные координаты к узлу и перерисуем
                    _dragNode.X = newX;
                    _dragNode.Y = newY;
                }
                var cmd = new MoveNodeCommand(_dragNode, _dragStart.X, _dragStart.Y, newX, newY);
                _cmd.ExecuteCommand(cmd);
                _dragNode = null;
                InvalidateCanvas();
                return;
            }

            if (e.Button == MouseButtons.Right)
            {
                //var p = ScreenToCanvas(e.Location);
                var hit = HitTestEdge(p);
                if (hit != null)
                {
                    _selectedEdgeId = hit.Id;
                    _canvasContext.Show(_canvas, e.Location);
                }
            }

            if (_isSelectingRect && e.Button == MouseButtons.Left)
            {
                _isSelectingRect = false;

                // преобразовать screen rect -> canvas rect и выбрать все узлы внутри
                var r = _selectionRectScreen;
                // преобразуем углы
                var p1 = ScreenToCanvas(new Point(r.Left, r.Top));
                var p2 = ScreenToCanvas(new Point(r.Right, r.Bottom));
                var canvasRect = RectangleF.FromLTRB(
                    Math.Min(p1.X, p2.X),
                    Math.Min(p1.Y, p2.Y),
                    Math.Max(p1.X, p2.X),
                    Math.Max(p1.Y, p2.Y));

                // функция, выбирающая узлы в canvasRect
                var nodesInRect = _diagram.Nodes.Where(n =>
                    n.X >= canvasRect.Left && n.X <= canvasRect.Right &&
                    n.Y >= canvasRect.Top && n.Y <= canvasRect.Bottom).ToList();

                // если без Ctrl/Shift — мы уже очистили выбор в MouseDown; если были модификаторы — добавляем
                foreach (var n in nodesInRect)
                    _selectedNodeIds.Add(n.Id);

                UpdatePropertyGridSelection();
                InvalidateCanvas();
                return;
            }
        }

        private void Canvas_MouseWheel(object sender, MouseEventArgs e)
        {
            var delta = e.Delta > 0 ? 1.1f : 0.9f;
            _scale *= delta;
            if (_scale < 0.2f) _scale = 0.2f;
            if (_scale > 4f) _scale = 4f;
            InvalidateCanvas();
        }

        #endregion

        #region File operations: New/Open/Save/Export

        private void NewDiagram()
        {
            if (AskSaveIfDirty()) return;
            _diagram = new Diagram();
            _cmd.Clear();
            _currentFile = null;
            ClearHighlights();
            InvalidateCanvas();
        }

        private bool AskSaveIfDirty()
        {
            var res = MessageBox.Show("Создать новый? Сохранить текущий файл?", "Новый", MessageBoxButtons.YesNoCancel);
            if (res == DialogResult.Cancel) return true;
            if (res == DialogResult.Yes) SaveDiagram();
            return false;
        }

        private void OpenDiagram()
        {
            using (var ofd = new OpenFileDialog { Filter = "Diagram files|*.cdg;*.json|All files|*.*" })
            {
                if (ofd.ShowDialog() != DialogResult.OK) return;
                var txt = File.ReadAllText(ofd.FileName);
                try
                {
                    _diagram = JsonConvert.DeserializeObject<Diagram>(txt) ?? new Diagram();
                    _currentFile = ofd.FileName;
                    _cmd.Clear();
                    ClearHighlights();
                    InvalidateCanvas();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Ошибка при загрузке: " + ex.Message);
                }
            }
        }

        private void SaveDiagram()
        {
            if (string.IsNullOrEmpty(_currentFile))
            {
                using (var sfd = new SaveFileDialog { Filter = "Diagram files|*.cdg;*.json", FileName = "diagram.cdg" })
                {
                    if (sfd.ShowDialog() != DialogResult.OK) return;
                    _currentFile = sfd.FileName;
                }
            }

            try
            {
                var json = JsonConvert.SerializeObject(_diagram, Formatting.Indented);
                File.WriteAllText(_currentFile, json);
                MessageBox.Show("Сохранено.");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка при сохранении: " + ex.Message);
            }
        }

        private void ExportPng()
        {
            using (var sfd = new SaveFileDialog { Filter = "PNG Image|*.png", FileName = "diagram.png" })
            {
                if (sfd.ShowDialog() != DialogResult.OK) return;

                // создаём bitmap размером как текущая видимая область канваса
                int w = Math.Max(1, _canvas.Width);
                int h = Math.Max(1, _canvas.Height);

                using (var bmp = new Bitmap(w, h))
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.White);
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                    // применяем ту же трансформацию, что и на холсте (scale и pan)
                    var m = g.Transform;
                    m.Reset();
                    m.Scale(_scale, _scale);
                    m.Translate(_panOffset.X, _panOffset.Y);
                    g.Transform = m;

                    // Рисуем ребра (стрелки) через DrawArrow - учитываем highlight/selected
                    foreach (var edge in _diagram.Edges)
                    {
                        var from = _diagram.Nodes.FirstOrDefault(n => n.Id == edge.From);
                        var to = _diagram.Nodes.FirstOrDefault(n => n.Id == edge.To);
                        if (from == null || to == null) continue;

                        bool isSelOrHighlight = (edge.Id == _selectedEdgeId) || edge.IsHighlighted;
                        DrawArrow(g, new PointF(from.X, from.Y), new PointF(to.X, to.Y), isSelOrHighlight);
                    }


                    // Рисуем узлы через ваш DrawNode (он уже рисует прямоугольники/закругления и цвет)
                    foreach (var node in _diagram.Nodes)
                    {
                        DrawNode(g, node);
                    }

                    // Сохраняем
                    bmp.Save(sfd.FileName, System.Drawing.Imaging.ImageFormat.Png);
                }

                MessageBox.Show("Экспорт завершён.");
            }
        }

        #endregion

        #region Selection / delete / property grid helper

        private void DeleteSelected()
        {
            // если выделено ребро — удаляем его
            if (_selectedEdgeId != Guid.Empty)
            {
                var edge = _diagram.Edges.FirstOrDefault(e => e.Id == _selectedEdgeId);
                if (edge != null)
                {
                    var cmdEdge = new RemoveEdgeCommand(_diagram, edge);
                    _cmd.ExecuteCommand(cmdEdge);
                    _selectedEdgeId = Guid.Empty;
                    InvalidateCanvas();
                    return;
                }
            }

            // если есть множественный выбор узлов — удаляем их
            var selNodes = GetSelectedNodes().ToList();
            if (selNodes.Count > 0)
            {
                // удаляем каждый узел (командно)
                foreach (var n in selNodes)
                {
                    var cmdMultiEdge = new RemoveNodeCommand(_diagram, n);
                    _cmd.ExecuteCommand(cmdMultiEdge);
                }
                _selectedNodeIds.Clear();
                _propGrid.SelectedObject = null;
                InvalidateCanvas();
                return;
            }

            //удаляем узел
            var sel = _propGrid.SelectedObject as NodeProxy;
            if (sel == null)
            {
                MessageBox.Show("Выберите узел в панели свойств.");
                return;
            }
            if (sel != null)
            {
                var node = sel.Node;
                var cmd2 = new RemoveNodeCommand(_diagram, node);
                _cmd.ExecuteCommand(cmd2);
                _propGrid.SelectedObject = null;
                InvalidateCanvas();
                return;
            }
            //var node = sel.Node;
            //var cmd = new RemoveNodeCommand(_diagram, node);
            //_cmd.ExecuteCommand(cmd);
            //_propGrid.SelectedObject = null;
            //InvalidateCanvas();
        }

        //private void ShowFmeaForm()
        //{
        //    var f = new FmeaForm(_diagram);
        //    f.ShowDialog();
        //    InvalidateCanvas();
        //}

        #endregion

        public class NodeProxy
        {
            public Node Node { get; }
            public NodeProxy(Node n) { Node = n; }

            [DisplayName("Название")]
            [Description("Название узла (краткое)")]
            public string Название { get => Node.Title; set => Node.Title = value; }

            [DisplayName("Описание")]
            [Description("Подробное описание причины или следствия")]
            public string Описание { get => Node.Description; set => Node.Description = value; }

            [DisplayName("Вес")]
            [Description("Произвольная числовая метрика (вес/важность узла)")]
            public float Вес { get => Node.Weight; set => Node.Weight = value; }

            [DisplayName("Цвет")]
            [Description("Выберите цвет узла")]
            public NodeColor Цвет
            {
                get => Node.ColorName;
                set => Node.ColorName = value;
            }

            [DisplayName("Категория")]
            [Description("Категория узла (используется для правил запрещённых связей)")]
            public NodeCategory Категория
            {
                get => Node.Category;
                set => Node.Category = value;
            }


            // Скрываем технические поля FMEA (оставляем в модели, но не показываем в PropertyGrid)
            [Browsable(false)]
            public int Severity { get => Node.Severity; set => Node.Severity = value; }
            [Browsable(false)]
            public int Occurrence { get => Node.Occurrence; set => Node.Occurrence = value; }
            [Browsable(false)]
            public int Detectability { get => Node.Detectability; set => Node.Detectability = value; }
        }

        // Reflection helper to enable double buffering on Panel
        private void DoubleBufferedControl(Control c, bool setting)
        {
            var prop = typeof(Control).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            prop.SetValue(c, setting, null);
        }

        private void CopySelected()
        {
            var selNodes = GetSelectedNodes().ToList();
            if (selNodes.Count == 0)
            {
                MessageBox.Show("Ничего не выбрано для копирования.");
                return;
            }

            var selIds = new HashSet<Guid>(selNodes.Select(n => n.Id));
            var sub = new Diagram();
            // копируем узлы (с их исходными Id — чтобы при вставке понять связи)
            foreach (var n in selNodes)
            {
                sub.Nodes.Add(new Node
                {
                    Id = n.Id,
                    Title = n.Title,
                    Description = n.Description,
                    X = n.X,
                    Y = n.Y,
                    Weight = n.Weight,
                    ColorName = n.ColorName,
                    Severity = n.Severity,
                    Occurrence = n.Occurrence,
                    Detectability = n.Detectability
                });
            }

            // копируем ребра только между выбранными узлами
            foreach (var e in _diagram.Edges)
            {
                if (selIds.Contains(e.From) && selIds.Contains(e.To))
                {
                    sub.Edges.Add(new Edge { Id = e.Id, From = e.From, To = e.To });
                }
            }

            var json = JsonConvert.SerializeObject(sub, Formatting.Indented);
            Clipboard.SetText(json);
            _statusLabel.Text = $"Скопировано {sub.Nodes.Count} узлов, {sub.Edges.Count} связей";
        }
        private IEnumerable<Node> GetSelectedNodes()
        {
            return _diagram.Nodes.Where(n => _selectedNodeIds.Contains(n.Id));
        }


        private void PasteFromClipboard()
        {
            if (!Clipboard.ContainsText()) { MessageBox.Show("Буфер пуст или не содержит диаграмму."); return; }

            string text = Clipboard.GetText();
            Diagram sub;
            try
            {
                sub = JsonConvert.DeserializeObject<Diagram>(text);
                if (sub == null || sub.Nodes.Count == 0) { MessageBox.Show("Буфер не содержит корректных данных."); return; }
            }
            catch
            {
                MessageBox.Show("Ошибка чтения данных из буфера.");
                return;
            }

            // создание новой мапы старого Id -> нового Id
            var idMap = new Dictionary<Guid, Guid>();
            // выбираем смещение: использовать текущую позицию мыши или фиксированный сдвиг
            float offsetX = 20f, offsetY = 20f;

            // если есть текущие выделенные узлы — можно вставлять рядом; для простоты используем смещение
            foreach (var n in sub.Nodes)
            {
                var newNode = new Node
                {
                    Id = Guid.NewGuid(),
                    Title = n.Title,
                    Description = n.Description,
                    X = n.X + offsetX,
                    Y = n.Y + offsetY,
                    Weight = n.Weight,
                    ColorName = n.ColorName,
                    Severity = n.Severity,
                    Occurrence = n.Occurrence,
                    Detectability = n.Detectability
                };
                idMap[n.Id] = newNode.Id;
                var cmd = new AddNodeCommand(_diagram, newNode);
                _cmd.ExecuteCommand(cmd);
                // обновляем offset чтобы при вставке большого набора объекты не наезжали
                offsetX += 10; offsetY += 10;
            }

            // вставка ребер между скопированными узлами
            foreach (var e in sub.Edges)
            {
                if (idMap.TryGetValue(e.From, out var nf) && idMap.TryGetValue(e.To, out var nt))
                {
                    var edge = new Edge { Id = Guid.NewGuid(), From = nf, To = nt };
                    var cmd = new AddEdgeCommand(_diagram, edge);
                    _cmd.ExecuteCommand(cmd);
                }
            }

            // после вставки — выделим новые узлы
            _selectedNodeIds.Clear();
            foreach (var newId in idMap.Values) _selectedNodeIds.Add(newId);

            UpdatePropertyGridSelection();
            InvalidateCanvas();
            _statusLabel.Text = $"Вставлено {idMap.Count} узлов";
        }

        private void UpdatePropertyGridSelection()
        {
            if (_selectedNodeIds.Count == 0)
            {
                _propGrid.SelectedObject = null;
                _propGridOldSnapshot = null;
            }
            else if (_selectedNodeIds.Count == 1)
            {
                var node = _diagram.Nodes.FirstOrDefault(n => _selectedNodeIds.Contains(n.Id));
                if (node != null)
                {
                    _propGrid.SelectedObject = new NodeProxy(node);
                    _propGridOldSnapshot = CloneNode(node);
                }
            }
            else
            {
                // для множественного выбора не показываем PropertyGrid (или можно показать массив объектов)
                _propGrid.SelectedObject = null;
                _propGridOldSnapshot = null;
            }
        }

        // Возвращает список путей (каждый путь — список node.Id) от источников к targetId.
        // maxPaths — ограничение по количеству найденных путей (во избежание combinatorial explosion).
        // maxDepth — максимальная длина пути.
        private List<List<Guid>> FindPredecessorPaths(Guid targetId, int maxPaths = 100, int maxDepth = 20)
        {
            var result = new List<List<Guid>>();
            if (targetId == Guid.Empty) return result;

            // Строим обратный adjacency (from <- to)
            var predecessors = new Dictionary<Guid, List<Guid>>();
            foreach (var e in _diagram.Edges)
            {
                if (!predecessors.ContainsKey(e.To)) predecessors[e.To] = new List<Guid>();
                predecessors[e.To].Add(e.From);
            }

            // BFS-like approach storing paths (queue of paths)
            var q = new Queue<List<Guid>>();
            q.Enqueue(new List<Guid> { targetId });

            while (q.Count > 0 && result.Count < maxPaths)
            {
                var path = q.Dequeue(); // path: [current, ..., target]
                var current = path[0];

                // if no predecessors -> we've reached a source (path complete). Reverse so source -> ... -> target
                if (!predecessors.ContainsKey(current) || predecessors[current].Count == 0 || path.Count - 1 >= maxDepth)
                {
                    // add reversed copy
                    var rev = path.ToArray().Reverse().ToList();
                    result.Add(rev);
                    continue;
                }

                // extend
                foreach (var p in predecessors[current])
                {
                    // avoid cycles in path
                    if (path.Contains(p)) continue;
                    var newPath = new List<Guid> { p };
                    newPath.AddRange(path);
                    q.Enqueue(newPath);
                    if (result.Count + q.Count > maxPaths * 5) // safe-guard
                    {
                        // stop expanding too much (protect from explosion)
                        break;
                    }
                }
            }
            return result;
        }

        private void DoTraceCause()
        {
            // Получим выбранный узел (если множественный выбор — берем первый)
            Guid targetId = Guid.Empty;
            if (_selectedNodeIds.Count == 1)
                targetId = _selectedNodeIds.First();
            else if (_propGrid.SelectedObject is NodeProxy np)
                targetId = np.Node.Id;
            else
            {
                MessageBox.Show("Выберите один узел (обычно красный) для трассировки причин.", "Трассировка", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // Очистим старые подсветки
            foreach (var n in _diagram.Nodes) n.IsHighlighted = false;
            foreach (var e in _diagram.Edges) e.IsHighlighted = false;

            // Найдём пути
            var paths = FindPredecessorPaths(targetId, maxPaths: 200, maxDepth: 25);

            // Подсветим узлы и ребра, которые участвуют в найденных путях
            var nodesInPaths = new HashSet<Guid>();
            var edgesInPaths = new HashSet<Guid>();
            foreach (var path in paths)
            {
                for (int i = 0; i < path.Count; i++)
                {
                    nodesInPaths.Add(path[i]);
                    if (i < path.Count - 1)
                    {
                        // edge from path[i] -> path[i+1]
                        var edge = _diagram.Edges.FirstOrDefault(ed => ed.From == path[i] && ed.To == path[i + 1]);
                        if (edge != null) edgesInPaths.Add(edge.Id);
                    }
                }
            }

            foreach (var id in nodesInPaths)
            {
                var node = _diagram.Nodes.FirstOrDefault(n => n.Id == id);
                if (node != null) node.IsHighlighted = true;
            }
            foreach (var id in edgesInPaths)
            {
                var edge = _diagram.Edges.FirstOrDefault(e => e.Id == id);
                if (edge != null) edge.IsHighlighted = true;
            }

            InvalidateCanvas();

            // Показать простую панель отчёта: количество путей, длину кратчайшего пути и список первичных причин
            var primaryCauses = paths.Select(p => p.First()).Distinct().ToList();
            string report = $"Найдено путей: {paths.Count}\nУникальных первичных причин: {primaryCauses.Count}\n\nПервые 10 первичных причин:\n";
            for (int i = 0; i < Math.Min(10, primaryCauses.Count); i++)
            {
                var n = _diagram.Nodes.FirstOrDefault(x => x.Id == primaryCauses[i]);
                report += $"{i + 1}. {n?.Title ?? primaryCauses[i].ToString()} (длина пути: {paths.Where(p => p.First() == primaryCauses[i]).Min(p => p.Count - 1)})\n";
            }
            MessageBox.Show(report, "Результат трассировки", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ClearHighlights()
        {
            foreach (var n in _diagram.Nodes) n.IsHighlighted = false;
            foreach (var e in _diagram.Edges) e.IsHighlighted = false;
            InvalidateCanvas();
        }

        // Возвращает true, если соединение from->to запрещено (и out reason содержит правило)
        private bool IsForbiddenConnection(Node from, Node to, out ForbiddenRule violatedRule)
        {
            violatedRule = null;
            if (from == null || to == null) return false;

            // если правила хранятся в _diagram (и вы добавили ForbiddenRules туда) — используйте их
            var rulesCollection = _diagram?.ForbiddenRules ?? _forbiddenRules;

            foreach (var r in rulesCollection)
            {
                if (r.FromCategory == from.Category && r.ToCategory == to.Category)
                {
                    violatedRule = r;
                    return true;
                }
            }

            // Пример простого правила: запрет "перескакивать уровни" 
            // if (Math.Abs((int)from.Category - (int)to.Category) > 2) { violatedRule = new ForbiddenRule{FromCategory=from.Category, ToCategory=to.Category, Reason="Слишком большая разница уровней"}; return true; }

            return false;
        }

        private void EditForbiddenRules()
        {
            var form = new ForbiddenRulesForm(_diagram?.ForbiddenRules ?? _forbiddenRules);
            if (form.ShowDialog() == DialogResult.OK)
            {
                // если правила встраиваются в diagram
                if (_diagram != null)
                    _diagram.ForbiddenRules = form.Rules;
                else
                    _forbiddenRules = form.Rules;
                MessageBox.Show("Правила сохранены.");
            }
        }

    }
}
