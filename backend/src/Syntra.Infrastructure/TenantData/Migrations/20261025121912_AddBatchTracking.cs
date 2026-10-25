using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Syntra.Infrastructure.TenantData.Migrations
{
    /// <inheritdoc />
    public partial class AddBatchTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StockMovements_ProductId_WarehouseId",
                table: "StockMovements");

            migrationBuilder.AddColumn<int>(
                name: "BatchId",
                table: "StockMovements",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BatchTracking",
                table: "Products",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<int>(
                name: "ProductBatchId",
                table: "GoodsReceiptLines",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProductBatches",
                columns: table => new
                {
                    ProductBatchId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    BatchNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ExpiryDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductBatches", x => x.ProductBatchId);
                    table.ForeignKey(
                        name: "FK_ProductBatches_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "ProductId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_BatchId",
                table: "StockMovements",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_ProductId_WarehouseId_BatchId",
                table: "StockMovements",
                columns: new[] { "ProductId", "WarehouseId", "BatchId" });

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReceiptLines_ProductBatchId",
                table: "GoodsReceiptLines",
                column: "ProductBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductBatches_ProductId_BatchNumber",
                table: "ProductBatches",
                columns: new[] { "ProductId", "BatchNumber" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_GoodsReceiptLines_ProductBatches_ProductBatchId",
                table: "GoodsReceiptLines",
                column: "ProductBatchId",
                principalTable: "ProductBatches",
                principalColumn: "ProductBatchId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StockMovements_ProductBatches_BatchId",
                table: "StockMovements",
                column: "BatchId",
                principalTable: "ProductBatches",
                principalColumn: "ProductBatchId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_GoodsReceiptLines_ProductBatches_ProductBatchId",
                table: "GoodsReceiptLines");

            migrationBuilder.DropForeignKey(
                name: "FK_StockMovements_ProductBatches_BatchId",
                table: "StockMovements");

            migrationBuilder.DropTable(
                name: "ProductBatches");

            migrationBuilder.DropIndex(
                name: "IX_StockMovements_BatchId",
                table: "StockMovements");

            migrationBuilder.DropIndex(
                name: "IX_StockMovements_ProductId_WarehouseId_BatchId",
                table: "StockMovements");

            migrationBuilder.DropIndex(
                name: "IX_GoodsReceiptLines_ProductBatchId",
                table: "GoodsReceiptLines");

            migrationBuilder.DropColumn(
                name: "BatchId",
                table: "StockMovements");

            migrationBuilder.DropColumn(
                name: "BatchTracking",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "ProductBatchId",
                table: "GoodsReceiptLines");

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_ProductId_WarehouseId",
                table: "StockMovements",
                columns: new[] { "ProductId", "WarehouseId" });
        }
    }
}
