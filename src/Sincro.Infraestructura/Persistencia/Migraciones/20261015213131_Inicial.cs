using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Sincro.Infraestructura.Persistencia.Migraciones
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cursores",
                columns: table => new
                {
                    nombre = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    valor = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cursores", x => x.nombre);
                });

            migrationBuilder.CreateTable(
                name: "mensajes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    clave_idempotencia = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    clave_orden = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    referencia = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    intentos = table.Column<int>(type: "integer", nullable: false),
                    proximo_intento = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    bloqueado_hasta = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ultimo_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    creado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mensajes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "pedidos_sincronizados",
                columns: table => new
                {
                    id_tienda = table.Column<long>(type: "bigint", nullable: false),
                    id_erp = table.Column<long>(type: "bigint", nullable: false),
                    ultima_accion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    actualizado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pedidos_sincronizados", x => x.id_tienda);
                });

            migrationBuilder.CreateTable(
                name: "registro",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    fecha = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    entidad = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    referencia = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    accion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    detalle = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_registro", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_mensajes_clave_idempotencia",
                table: "mensajes",
                column: "clave_idempotencia",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_mensajes_estado_proximo_intento",
                table: "mensajes",
                columns: new[] { "estado", "proximo_intento" });

            migrationBuilder.CreateIndex(
                name: "ix_mensajes_un_pendiente_por_entidad",
                table: "mensajes",
                column: "clave_orden",
                unique: true,
                filter: "estado = 'Pendiente'");

            migrationBuilder.CreateIndex(
                name: "ix_registro_entidad_referencia",
                table: "registro",
                columns: new[] { "entidad", "referencia" });

            migrationBuilder.CreateIndex(
                name: "ix_registro_fecha",
                table: "registro",
                column: "fecha");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cursores");

            migrationBuilder.DropTable(
                name: "mensajes");

            migrationBuilder.DropTable(
                name: "pedidos_sincronizados");

            migrationBuilder.DropTable(
                name: "registro");
        }
    }
}
