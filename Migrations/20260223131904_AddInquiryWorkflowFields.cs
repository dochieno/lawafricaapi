using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LawAfrica.API.Migrations
{
    /// <inheritdoc />
    public partial class AddInquiryWorkflowFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CloseNote",
                table: "LawyerInquiries",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ClosedAtUtc",
                table: "LawyerInquiries",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ClosedByUserId",
                table: "LawyerInquiries",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ContactedAtUtc",
                table: "LawyerInquiries",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "InProgressAtUtc",
                table: "LawyerInquiries",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastStatusChangedAtUtc",
                table: "LawyerInquiries",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Outcome",
                table: "LawyerInquiries",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RatedAtUtc",
                table: "LawyerInquiries",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RatingComment",
                table: "LawyerInquiries",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RatingStars",
                table: "LawyerInquiries",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CloseNote",
                table: "LawyerInquiries");

            migrationBuilder.DropColumn(
                name: "ClosedAtUtc",
                table: "LawyerInquiries");

            migrationBuilder.DropColumn(
                name: "ClosedByUserId",
                table: "LawyerInquiries");

            migrationBuilder.DropColumn(
                name: "ContactedAtUtc",
                table: "LawyerInquiries");

            migrationBuilder.DropColumn(
                name: "InProgressAtUtc",
                table: "LawyerInquiries");

            migrationBuilder.DropColumn(
                name: "LastStatusChangedAtUtc",
                table: "LawyerInquiries");

            migrationBuilder.DropColumn(
                name: "Outcome",
                table: "LawyerInquiries");

            migrationBuilder.DropColumn(
                name: "RatedAtUtc",
                table: "LawyerInquiries");

            migrationBuilder.DropColumn(
                name: "RatingComment",
                table: "LawyerInquiries");

            migrationBuilder.DropColumn(
                name: "RatingStars",
                table: "LawyerInquiries");
        }
    }
}
