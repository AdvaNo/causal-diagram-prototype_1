using System;
using System.Collections.Generic;
using System.Windows.Forms;


namespace CausalDiagram_1
{
    public class ForbiddenRulesForm : Form
    {
        private DataGridView _grid;
        private Button _btnOk;
        private Button _btnCancel;
        public List<ForbiddenRule> Rules { get; private set; }

        public ForbiddenRulesForm(List<ForbiddenRule> rules)
        {
            Text = "Правила запрещённых связей";
            Width = 600;
            Height = 400;
            Rules = rules != null ? new List<ForbiddenRule>(rules) : new List<ForbiddenRule>();

            _grid = new DataGridView { Dock = DockStyle.Top, Height = 300, AutoGenerateColumns = false };
            _grid.Columns.Add(new DataGridViewComboBoxColumn
            {
                DataSource = Enum.GetValues(typeof(NodeCategory)),
                HeaderText = "От (FromCategory)",
                DataPropertyName = "FromCategory",
                Width = 200
            });
            _grid.Columns.Add(new DataGridViewComboBoxColumn
            {
                DataSource = Enum.GetValues(typeof(NodeCategory)),
                HeaderText = "К (ToCategory)",
                DataPropertyName = "ToCategory",
                Width = 200
            });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Причина", DataPropertyName = "Reason", Width = 400 });

            _grid.DataSource = new BindingSource { DataSource = Rules };

            _btnOk = new Button { Text = "OK", Dock = DockStyle.Left, Width = 80 };
            _btnCancel = new Button { Text = "Отмена", Dock = DockStyle.Right, Width = 80 };

            _btnOk.Click += (s, e) => { this.DialogResult = DialogResult.OK; Close(); };
            _btnCancel.Click += (s, e) => { this.DialogResult = DialogResult.Cancel; Close(); };

            var panel = new Panel { Dock = DockStyle.Bottom, Height = 40 };
            panel.Controls.Add(_btnOk);
            panel.Controls.Add(_btnCancel);

            Controls.Add(_grid);
            Controls.Add(panel);
        }

    }
}
