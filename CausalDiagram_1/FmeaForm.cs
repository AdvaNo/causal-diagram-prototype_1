using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace CausalDiagram_1
{
    public class FmeaForm : Form
    {
        private readonly Diagram _diagram;
        private DataGridView _grid;
        private Button _btnCalc;
        private Button _btnExport;

        public FmeaForm(Diagram diagram)
        {
            _diagram = diagram;
            InitializeComponents();
            LoadGrid();
        }

        private void InitializeComponents()
        {
            Text = "FMEA — расчёт RPN";
            Width = 700;
            Height = 400;

            _grid = new DataGridView
            {
                Dock = DockStyle.Top,
                Height = 280,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                AllowUserToAddRows = false
            };
            Controls.Add(_grid);

            _btnCalc = new Button { Text = "Рассчитать RPN", Dock = DockStyle.Left, Width = 150 };
            _btnCalc.Click += (s, e) => { RecalculateRpnInGrid(); MessageBox.Show("RPN пересчитан в таблице."); };
            Controls.Add(_btnCalc);

            _btnExport = new Button { Text = "Экспорт CSV", Dock = DockStyle.Right, Width = 150 };
            _btnExport.Click += (s, e) => ExportCsv();
            Controls.Add(_btnExport);
        }

        private void LoadGrid()
        {
            _grid.Columns.Add("Title", "Узел");
            _grid.Columns.Add("Severity", "S (Severity)");
            _grid.Columns.Add("Occurrence", "O (Occurrence)");
            _grid.Columns.Add("Detectability", "D (Detectability)");
            _grid.Columns.Add("RPN", "RPN");

            foreach (var n in _diagram.Nodes)
            {
                int row = _grid.Rows.Add();
                var r = _grid.Rows[row];
                r.Cells[0].Value = n.Title;
                r.Cells[1].Value = n.Severity;
                r.Cells[2].Value = n.Occurrence;
                r.Cells[3].Value = n.Detectability;
                r.Cells[4].Value = n.Rpn;
            }

            _grid.CellEndEdit += (s, e) =>
            {
                if (e.RowIndex < 0) return;
                var row = _grid.Rows[e.RowIndex];
                int sVal = ParseIntOrDefault(row.Cells[1].Value, 1);
                int oVal = ParseIntOrDefault(row.Cells[2].Value, 1);
                int dVal = ParseIntOrDefault(row.Cells[3].Value, 1);

                var node = _diagram.Nodes.ElementAtOrDefault(e.RowIndex);
                if (node != null)
                {
                    node.Severity = ClampInt(sVal, 1, 10);
                    node.Occurrence = ClampInt(oVal, 1, 10);
                    node.Detectability = ClampInt(dVal, 1, 10);
                    row.Cells[4].Value = node.Rpn;
                }
            };
        }

        private static int ParseIntOrDefault(object o, int def)
        {
            if (o == null) return def;
            if (int.TryParse(o.ToString(), out int v)) return v;
            return def;
        }

        private static int ClampInt(int v, int min, int max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        private void RecalculateRpnInGrid()
        {
            for (int i = 0; i < _diagram.Nodes.Count; i++)
            {
                var node = _diagram.Nodes[i];

                if (i >= 0 && i < _grid.Rows.Count)
                {
                    var row = _grid.Rows[i];
                    row.Cells[4].Value = node.Rpn;
                }
            }
        }

        private void ExportCsv()
        {
            using (var sfd = new SaveFileDialog { Filter = "CSV|*.csv", FileName = "fmea_report.csv" })
            {
                if (sfd.ShowDialog() != DialogResult.OK) return;
                using (var sw = new StreamWriter(sfd.FileName))
                {
                    sw.WriteLine("Title,Severity,Occurrence,Detectability,RPN");
                    foreach (var n in _diagram.Nodes)
                    {
                        sw.WriteLine($"\"{n.Title}\",{n.Severity},{n.Occurrence},{n.Detectability},{n.Rpn}");
                    }
                }
                MessageBox.Show("Экспорт выполнен.");
            }
        }
    }
}
