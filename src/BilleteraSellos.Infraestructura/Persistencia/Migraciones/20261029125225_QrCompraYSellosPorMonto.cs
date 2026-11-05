using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace BilleteraSellos.Infraestructura.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class QrCompraYSellosPorMonto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CompraMinima",
                table: "Programas",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MontoPorSello",
                table: "Programas",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Cantidad",
                table: "Movimientos",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<decimal>(
                name: "Monto",
                table: "Movimientos",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ComprasQr",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ComercioId = table.Column<int>(type: "integer", nullable: false),
                    UsuarioId = table.Column<int>(type: "integer", nullable: false),
                    CodigoHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Monto = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    Sellos = table.Column<int>(type: "integer", nullable: false),
                    CreadoEn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    VenceEn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UsadaEn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClienteId = table.Column<int>(type: "integer", nullable: true),
                    AnuladaEn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComprasQr", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ComprasQr_Clientes_ClienteId",
                        column: x => x.ClienteId,
                        principalTable: "Clientes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ComprasQr_Comercios_ComercioId",
                        column: x => x.ComercioId,
                        principalTable: "Comercios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ComprasQr_Usuarios_UsuarioId",
                        column: x => x.UsuarioId,
                        principalTable: "Usuarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ComprasQr_ClienteId",
                table: "ComprasQr",
                column: "ClienteId");

            migrationBuilder.CreateIndex(
                name: "IX_ComprasQr_CodigoHash",
                table: "ComprasQr",
                column: "CodigoHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ComprasQr_ComercioId_CreadoEn",
                table: "ComprasQr",
                columns: new[] { "ComercioId", "CreadoEn" });

            migrationBuilder.CreateIndex(
                name: "IX_ComprasQr_UsuarioId",
                table: "ComprasQr",
                column: "UsuarioId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ComprasQr");

            migrationBuilder.DropColumn(
                name: "CompraMinima",
                table: "Programas");

            migrationBuilder.DropColumn(
                name: "MontoPorSello",
                table: "Programas");

            migrationBuilder.DropColumn(
                name: "Cantidad",
                table: "Movimientos");

            migrationBuilder.DropColumn(
                name: "Monto",
                table: "Movimientos");
        }
    }
}
