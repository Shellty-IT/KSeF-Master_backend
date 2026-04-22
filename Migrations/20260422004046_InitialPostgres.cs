using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace KSeF_Backend.Migrations
{
    /// <inheritdoc />
    public partial class InitialPostgres : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CompanyProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    CompanyName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Nip = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    KsefTokenEncrypted = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    AuthMethod = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "token"),
                    KsefEnvironment = table.Column<string>(type: "text", nullable: false),
                    CertificateEncrypted = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: true),
                    PrivateKeyEncrypted = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: true),
                    CertificatePasswordEncrypted = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    LastSuccessfulAuthMethod = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompanyProfiles_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Invoices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CompanyProfileId = table.Column<int>(type: "integer", nullable: false),
                    KsefReferenceNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Nip = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    InvoiceType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Direction = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    InvoiceNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SellerNip = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    SellerName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    BuyerNip = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    BuyerName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    NetAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    VatAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    GrossAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    InvoiceDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AcquisitionTimestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SyncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    XmlContent = table.Column<string>(type: "text", nullable: true),
                    KsefEnvironment = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Invoices_CompanyProfiles_CompanyProfileId",
                        column: x => x.CompanyProfileId,
                        principalTable: "CompanyProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CompanyProfiles_Nip",
                table: "CompanyProfiles",
                column: "Nip");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyProfiles_UserId",
                table: "CompanyProfiles",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_CompanyProfileId",
                table: "Invoices",
                column: "CompanyProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_CompanyProfileId_Direction",
                table: "Invoices",
                columns: new[] { "CompanyProfileId", "Direction" });

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_CompanyProfileId_InvoiceDate",
                table: "Invoices",
                columns: new[] { "CompanyProfileId", "InvoiceDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_KsefReferenceNumber",
                table: "Invoices",
                column: "KsefReferenceNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Invoices");

            migrationBuilder.DropTable(
                name: "CompanyProfiles");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
