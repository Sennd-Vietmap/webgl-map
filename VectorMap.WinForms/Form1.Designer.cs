namespace VectorMap.WinForms;

partial class Form1
{
    /// <summary>
    ///  Required designer variable.
    /// </summary>
    private System.ComponentModel.IContainer components = null;

    /// <summary>
    ///  Clean up any resources being used.
    /// </summary>
    /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    #region Windows Form Designer generated code

    /// <summary>
    ///  Required method for Designer support - do not modify
    ///  the contents of this method with the code editor.
    /// </summary>
    private void InitializeComponent()
    {
        mapControl1 = new MapControl();
        SuspendLayout();
        // 
        // mapControl1
        // 
        mapControl1.API = OpenTK.Windowing.Common.ContextAPI.OpenGL;
        mapControl1.APIVersion = new Version(3, 3, 0, 0);
        mapControl1.Dock = DockStyle.Fill;
        mapControl1.Flags = OpenTK.Windowing.Common.ContextFlags.Default;
        mapControl1.IsEventDriven = true;
        mapControl1.Location = new Point(0, 0);
        mapControl1.Name = "mapControl1";
        mapControl1.Profile = OpenTK.Windowing.Common.ContextProfile.Core;
        mapControl1.SharedContext = null;
        mapControl1.Size = new Size(800, 450);
        mapControl1.TabIndex = 0;
        // 
        // Form1
        // 
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(800, 450);
        Controls.Add(mapControl1);
        Name = "Form1";
        Text = "Form1";
        ResumeLayout(false);
    }

    #endregion

    private MapControl mapControl1;
}
