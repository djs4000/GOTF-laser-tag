using System;
using System.Drawing;
using System.Text.Json;
using System.Windows.Forms;
using LaserTag.Defusal.Domain;

namespace LaserTag.Defusal.Ui;

public partial class MatchResultForm : Form
{
    private Label? lblWinningTeamStatic;
    private Label? lblWinningRoleStatic;
    private Label? lblReasonStatic;
    private Label? lblFinalPayloadStatic;
    private Label? lblWinningTeam;
    private Label? lblWinningRole;
    private Label? lblReason;
    private TextBox? txtFinalPayload;
    private Button? btnClose;
    private System.ComponentModel.IContainer? components = null;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        this.lblWinningTeamStatic = new System.Windows.Forms.Label();
        this.lblWinningRoleStatic = new System.Windows.Forms.Label();
        this.lblReasonStatic = new System.Windows.Forms.Label();
        this.lblFinalPayloadStatic = new System.Windows.Forms.Label();
        this.lblWinningTeam = new System.Windows.Forms.Label();
        this.lblWinningRole = new System.Windows.Forms.Label();
        this.lblReason = new System.Windows.Forms.Label();
        this.txtFinalPayload = new System.Windows.Forms.TextBox();
        this.btnClose = new System.Windows.Forms.Button();
        this.SuspendLayout();
        // 
        // lblWinningTeamStatic
        // 
        this.lblWinningTeamStatic.AutoSize = true;
        this.lblWinningTeamStatic.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point);
        this.lblWinningTeamStatic.Location = new System.Drawing.Point(12, 15);
        this.lblWinningTeamStatic.Name = "lblWinningTeamStatic";
        this.lblWinningTeamStatic.Size = new System.Drawing.Size(92, 15);
        this.lblWinningTeamStatic.TabIndex = 0;
        this.lblWinningTeamStatic.Text = "Winning Team:";
        // 
        // lblWinningRoleStatic
        // 
        this.lblWinningRoleStatic.AutoSize = true;
        this.lblWinningRoleStatic.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point);
        this.lblWinningRoleStatic.Location = new System.Drawing.Point(12, 40);
        this.lblWinningRoleStatic.Name = "lblWinningRoleStatic";
        this.lblWinningRoleStatic.Size = new System.Drawing.Size(35, 15);
        this.lblWinningRoleStatic.TabIndex = 1;
        this.lblWinningRoleStatic.Text = "Role:";
        // 
        // lblReasonStatic
        // 
        this.lblReasonStatic.AutoSize = true;
        this.lblReasonStatic.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point);
        this.lblReasonStatic.Location = new System.Drawing.Point(12, 65);
        this.lblReasonStatic.Name = "lblReasonStatic";
        this.lblReasonStatic.Size = new System.Drawing.Size(51, 15);
        this.lblReasonStatic.TabIndex = 2;
        this.lblReasonStatic.Text = "Reason:";
        // 
        // lblFinalPayloadStatic
        // 
        this.lblFinalPayloadStatic.AutoSize = true;
        this.lblFinalPayloadStatic.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point);
        this.lblFinalPayloadStatic.Location = new System.Drawing.Point(12, 90);
        this.lblFinalPayloadStatic.Name = "lblFinalPayloadStatic";
        this.lblFinalPayloadStatic.Size = new System.Drawing.Size(83, 15);
        this.lblFinalPayloadStatic.TabIndex = 3;
        this.lblFinalPayloadStatic.Text = "Final Payload:";
        // 
        // lblWinningTeam
        // 
        this.lblWinningTeam.AutoSize = true;
        this.lblWinningTeam.Location = new System.Drawing.Point(110, 15);
        this.lblWinningTeam.Name = "lblWinningTeam";
        this.lblWinningTeam.Size = new System.Drawing.Size(38, 15);
        this.lblWinningTeam.TabIndex = 4;
        this.lblWinningTeam.Text = "label1";
        // 
        // lblWinningRole
        // 
        this.lblWinningRole.AutoSize = true;
        this.lblWinningRole.Location = new System.Drawing.Point(110, 40);
        this.lblWinningRole.Name = "lblWinningRole";
        this.lblWinningRole.Size = new System.Drawing.Size(38, 15);
        this.lblWinningRole.TabIndex = 5;
        this.lblWinningRole.Text = "label2";
        // 
        // lblReason
        // 
        this.lblReason.AutoSize = true;
        this.lblReason.Location = new System.Drawing.Point(110, 65);
        this.lblReason.Name = "lblReason";
        this.lblReason.Size = new System.Drawing.Size(38, 15);
        this.lblReason.TabIndex = 6;
        this.lblReason.Text = "label3";
        // 
        // txtFinalPayload
        // 
        this.txtFinalPayload.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
        this.txtFinalPayload.Location = new System.Drawing.Point(12, 108);
        this.txtFinalPayload.Multiline = true;
        this.txtFinalPayload.Name = "txtFinalPayload";
        this.txtFinalPayload.ReadOnly = true;
        this.txtFinalPayload.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
        this.txtFinalPayload.Size = new System.Drawing.Size(460, 192);
        this.txtFinalPayload.TabIndex = 7;
        // 
        // btnClose
        // 
        this.btnClose.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
        this.btnClose.Location = new System.Drawing.Point(397, 306);
        this.btnClose.Name = "btnClose";
        this.btnClose.Size = new System.Drawing.Size(75, 23);
        this.btnClose.TabIndex = 8;
        this.btnClose.Text = "Close";
        this.btnClose.UseVisualStyleBackColor = true;
        this.btnClose.Click += new System.EventHandler(this.btnClose_Click);
        // 
        // MatchResultForm
        // 
        this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
        this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
        this.ClientSize = new System.Drawing.Size(484, 341);
        this.Controls.Add(this.btnClose);
        this.Controls.Add(this.txtFinalPayload);
        this.Controls.Add(this.lblReason);
        this.Controls.Add(this.lblWinningRole);
        this.Controls.Add(this.lblWinningTeam);
        this.Controls.Add(this.lblFinalPayloadStatic);
        this.Controls.Add(this.lblReasonStatic);
        this.Controls.Add(this.lblWinningRoleStatic);
        this.Controls.Add(this.lblWinningTeamStatic);
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.MinimumSize = new System.Drawing.Size(350, 300);
        this.Name = "MatchResultForm";
        this.ShowIcon = false;
        this.Text = "Match Results";
        this.ResumeLayout(false);
        this.PerformLayout();
    }

    private static readonly JsonSerializerOptions JsonSerializerOptions = new() { WriteIndented = true };

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    public MatchResultForm(MatchStateSnapshot snapshot, string attackingTeam, string defendingTeam)
    {
        InitializeComponent();
        RenderData(snapshot, attackingTeam, defendingTeam);
    }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

    private void RenderData(MatchStateSnapshot snapshot, string attackingTeam, string defendingTeam)
    {
        var combinedPayload = snapshot.LatestOutboundRelayPayload;
        var winner = snapshot.WinnerTeam
            ?? combinedPayload?.Match?.WinnerTeam;
        if (string.IsNullOrWhiteSpace(winner))
        {
            if (lblWinningTeam is not null) lblWinningTeam.Text = "Unknown";
            if (lblWinningRole is not null) lblWinningRole.Text = "Unknown";
        }
        else
        {
            if (lblWinningTeam is not null) lblWinningTeam.Text = winner;
            if (lblWinningRole is not null)
            {
                lblWinningRole.Text = string.Equals(winner, attackingTeam, StringComparison.OrdinalIgnoreCase)
                    ? "Attacking"
                    : "Defending";
            }
        }

        if (lblReason is not null) lblReason.Text = GetWinnerReasonText(snapshot, combinedPayload);

        if (txtFinalPayload is not null)
        {
            if (combinedPayload is not null)
            {
                txtFinalPayload.Text = JsonSerializer.Serialize(combinedPayload, JsonSerializerOptions);
            }
            else
            {
                txtFinalPayload.Text = "No combined relay payload available.";
            }
        }
    }

    private static string GetWinnerReasonText(MatchStateSnapshot snapshot, CombinedRelayPayload? payload)
    {
        var reason = snapshot.WinnerReason ?? payload?.WinnerReason;
        if (reason is not null)
        {
            return reason.Value switch
            {
                WinnerReason.TeamElimination => "Team elimination",
                WinnerReason.ObjectiveDetonated => "Bomb detonated",
                WinnerReason.ObjectiveDefused => "Bomb defused",
                WinnerReason.TimeExpiration => "Time expired (no plant)",
                _ => reason.Value.ToString()
            };
        }

        return DeriveFallbackReason(snapshot);
    }

    private static string DeriveFallbackReason(MatchStateSnapshot snapshot)
    {
        if (snapshot.LastActionDescription.StartsWith("Triggering end:", StringComparison.OrdinalIgnoreCase))
        {
            return snapshot.LastActionDescription["Triggering end:".Length..].Trim();
        }

        return snapshot.PropState switch
        {
            PropState.Detonated => "Bomb Detonated",
            PropState.Defused => "Bomb Defused",
            _ => "Time Expired / Host Ended"
        };
    }

    private void btnClose_Click(object? sender, EventArgs e)
    {
        Close();
    }
}
