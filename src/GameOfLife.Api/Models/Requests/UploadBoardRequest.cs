using System.ComponentModel.DataAnnotations;

namespace GameOfLife.Api.Models.Requests;

public sealed class UploadBoardRequest
{
    /// <summary>
    /// 2-D grid of cells. Each inner array is a row; <c>true</c> = alive, <c>false</c> = dead.
    /// All rows must have equal length. Maximum dimension: 1 000 × 1 000.
    /// </summary>
    /// <example>[[false,true,false],[false,false,true],[true,true,true]]</example>
    [Required]
    public bool[][] Cells { get; set; } = [];
}
