using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BilleteraSellos.Infraestructura.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class ResenasYTarjetasDePapel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ResenaPedidaEn",
                table: "Tarjetas",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ResenaRespondidaEn",
                table: "Tarjetas",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ResenaRespuesta",
                table: "Tarjetas",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PedirResenaTrasVisitas",
                table: "Programas",
                type: "integer",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Mensaje",
                table: "Avisos",
                type: "character varying(600)",
                maxLength: 600,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(300)",
                oldMaxLength: 300);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ResenaPedidaEn",
                table: "Tarjetas");

            migrationBuilder.DropColumn(
                name: "ResenaRespondidaEn",
                table: "Tarjetas");

            migrationBuilder.DropColumn(
                name: "ResenaRespuesta",
                table: "Tarjetas");

            migrationBuilder.DropColumn(
                name: "PedirResenaTrasVisitas",
                table: "Programas");

            migrationBuilder.AlterColumn<string>(
                name: "Mensaje",
                table: "Avisos",
                type: "character varying(300)",
                maxLength: 300,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(600)",
                oldMaxLength: 600);
        }
    }
}
