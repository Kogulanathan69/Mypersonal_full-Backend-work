using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BuildMind.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLandMetadataAndConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "location_latitude",
                table: "land_revisions",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "location_locality_id",
                table: "land_revisions",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "location_locality_name",
                table: "land_revisions",
                type: "varchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "location_longitude",
                table: "land_revisions",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "location_source",
                table: "land_revisions",
                type: "varchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "location_verification_provider",
                table: "land_revisions",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "location_verification_status",
                table: "land_revisions",
                type: "varchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "location_verified_at_utc",
                table: "land_revisions",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "perimeter_m",
                table: "land_revisions",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "reported_area_m2",
                table: "land_revisions",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "reported_area_original_value",
                table: "land_revisions",
                type: "double",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reported_area_review_status",
                table: "land_revisions",
                type: "varchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "reported_area_source_file_id",
                table: "land_revisions",
                type: "char(36)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "reported_area_source_page_number",
                table: "land_revisions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reported_area_source_reference_text",
                table: "land_revisions",
                type: "varchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reported_area_unit",
                table: "land_revisions",
                type: "varchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_land_revisions_area",
                table: "land_revisions",
                sql: "area_m2 > 0 AND (perimeter_m IS NULL OR perimeter_m > 0)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_land_revisions_edge_count",
                table: "land_revisions",
                sql: "edge_count >= 3");

            migrationBuilder.AddCheckConstraint(
                name: "ck_land_revisions_location",
                table: "land_revisions",
                sql: "(\r\n    location_locality_id IS NULL\r\n    AND location_locality_name IS NULL\r\n    AND location_latitude IS NULL\r\n    AND location_longitude IS NULL\r\n    AND location_source IS NULL\r\n    AND location_verification_status IS NULL\r\n    AND location_verification_provider IS NULL\r\n    AND location_verified_at_utc IS NULL\r\n)\r\nOR\r\n(\r\n    location_locality_name IS NOT NULL\r\n    AND CHAR_LENGTH(TRIM(location_locality_name)) BETWEEN 1 AND 200\r\n    AND (location_locality_id IS NULL OR CHAR_LENGTH(TRIM(location_locality_id)) BETWEEN 1 AND 100)\r\n    AND location_source IN ('UserSelected', 'SurveyDocument', 'GeocodingProvider')\r\n    AND location_verification_status IN ('NotVerified', 'Verified')\r\n    AND (\r\n        (location_latitude IS NULL AND location_longitude IS NULL)\r\n        OR (location_latitude IS NOT NULL AND location_longitude IS NOT NULL\r\n            AND location_latitude BETWEEN -90 AND 90 AND location_longitude BETWEEN -180 AND 180)\r\n    )\r\n    AND (\r\n        (location_verification_status = 'NotVerified' AND location_verification_provider IS NULL AND location_verified_at_utc IS NULL)\r\n        OR (location_verification_status = 'Verified' AND location_source = 'GeocodingProvider'\r\n            AND location_verification_provider IS NOT NULL\r\n            AND CHAR_LENGTH(TRIM(location_verification_provider)) BETWEEN 1 AND 100\r\n            AND location_verified_at_utc IS NOT NULL)\r\n    )\r\n)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_land_revisions_reported_area",
                table: "land_revisions",
                sql: "(\r\n    reported_area_original_value IS NULL\r\n    AND reported_area_unit IS NULL\r\n    AND reported_area_m2 IS NULL\r\n    AND reported_area_source_file_id IS NULL\r\n    AND reported_area_source_page_number IS NULL\r\n    AND reported_area_source_reference_text IS NULL\r\n    AND reported_area_review_status IS NULL\r\n)\r\nOR\r\n(\r\n    reported_area_original_value IS NOT NULL\r\n    AND reported_area_original_value > 0\r\n    AND reported_area_unit IN ('M2', 'FT2', 'Unknown')\r\n    AND reported_area_source_file_id IS NOT NULL\r\n    AND reported_area_review_status IN ('Unreviewed', 'ManualReviewRequired', 'ManuallyReviewed')\r\n    AND (\r\n        (reported_area_unit = 'Unknown' AND reported_area_m2 IS NULL)\r\n        OR (reported_area_unit IN ('M2', 'FT2') AND reported_area_m2 IS NOT NULL AND reported_area_m2 > 0)\r\n    )\r\n    AND (reported_area_source_page_number IS NULL OR reported_area_source_page_number > 0)\r\n)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_land_revisions_revision_number",
                table: "land_revisions",
                sql: "revision_number > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_land_revisions_area",
                table: "land_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_land_revisions_edge_count",
                table: "land_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_land_revisions_location",
                table: "land_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_land_revisions_reported_area",
                table: "land_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_land_revisions_revision_number",
                table: "land_revisions");

            migrationBuilder.DropColumn(
                name: "location_latitude",
                table: "land_revisions");

            migrationBuilder.DropColumn(
                name: "location_locality_id",
                table: "land_revisions");

            migrationBuilder.DropColumn(
                name: "location_locality_name",
                table: "land_revisions");

            migrationBuilder.DropColumn(
                name: "location_longitude",
                table: "land_revisions");

            migrationBuilder.DropColumn(
                name: "location_source",
                table: "land_revisions");

            migrationBuilder.DropColumn(
                name: "location_verification_provider",
                table: "land_revisions");

            migrationBuilder.DropColumn(
                name: "location_verification_status",
                table: "land_revisions");

            migrationBuilder.DropColumn(
                name: "location_verified_at_utc",
                table: "land_revisions");

            migrationBuilder.DropColumn(
                name: "perimeter_m",
                table: "land_revisions");

            migrationBuilder.DropColumn(
                name: "reported_area_m2",
                table: "land_revisions");

            migrationBuilder.DropColumn(
                name: "reported_area_original_value",
                table: "land_revisions");

            migrationBuilder.DropColumn(
                name: "reported_area_review_status",
                table: "land_revisions");

            migrationBuilder.DropColumn(
                name: "reported_area_source_file_id",
                table: "land_revisions");

            migrationBuilder.DropColumn(
                name: "reported_area_source_page_number",
                table: "land_revisions");

            migrationBuilder.DropColumn(
                name: "reported_area_source_reference_text",
                table: "land_revisions");

            migrationBuilder.DropColumn(
                name: "reported_area_unit",
                table: "land_revisions");
        }
    }
}
