using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CsRaiders.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Email_DeadLetterStore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "dead_letter_emails",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient = table.Column<string>(type: "citext", nullable: false),
                    subject = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    purpose = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    error_reason = table.Column<string>(type: "text", nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    correlation_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    failed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dead_letter_emails", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_dead_letter_emails_failed_at",
                table: "dead_letter_emails",
                column: "failed_at",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_dead_letter_emails_recipient",
                table: "dead_letter_emails",
                column: "recipient");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "dead_letter_emails");
        }
    }
}
