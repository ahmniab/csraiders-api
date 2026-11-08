using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CsRaiders.Api.Infrastructure.Persistence.Migrations;

public partial class Auth_SecurityHardening : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<short>("pepper_version", "users", type: "smallint", nullable: false, defaultValue: (short)1);
        migrationBuilder.DropIndex("ux_login_attempts_email", "login_attempts");
        migrationBuilder.CreateIndex("ux_login_attempts_email_ip", "login_attempts", new[] { "email", "last_ip" }, unique: true);
        migrationBuilder.CreateTable(name: "app_settings", columns: table => new { key = table.Column<string>("text", nullable: false), value = table.Column<string>("text", nullable: false), updated_at = table.Column<DateTimeOffset>("timestamp with time zone", nullable: false) }, constraints: table => table.PrimaryKey("pk_app_settings", x => x.key));
        migrationBuilder.InsertData("app_settings", new[] { "key", "value", "updated_at" }, new object[] { "session.multi_max_concurrent", "3", DateTimeOffset.UtcNow });
        migrationBuilder.CreateTable(name: "security_events", columns: table => new { id = table.Column<Guid>("uuid", nullable: false), event_name = table.Column<string>("character varying(100)", maxLength: 100, nullable: false), user_id = table.Column<Guid>("uuid", nullable: true), email = table.Column<string>("citext", nullable: true), ip = table.Column<System.Net.IPAddress>("inet", nullable: true), details = table.Column<string>("jsonb", nullable: false), created_at = table.Column<DateTimeOffset>("timestamp with time zone", nullable: false), xmin = table.Column<uint>("xid", rowVersion: true, nullable: false) }, constraints: table => { table.PrimaryKey("pk_security_events", x => x.id); table.ForeignKey("fk_security_events_users_user_id", x => x.user_id, "users", "id", onDelete: ReferentialAction.SetNull); });
        migrationBuilder.CreateIndex("ix_security_events_created_at", "security_events", "created_at");
        migrationBuilder.CreateIndex("ix_security_events_user_id", "security_events", "user_id");
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("security_events");
        migrationBuilder.DropTable("app_settings");
        migrationBuilder.DropIndex("ux_login_attempts_email_ip", "login_attempts");
        migrationBuilder.CreateIndex("ux_login_attempts_email", "login_attempts", "email", unique: true);
        migrationBuilder.DropColumn("pepper_version", "users");
    }
}
