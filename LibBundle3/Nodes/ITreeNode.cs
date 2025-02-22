﻿using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using SystemExtensions.Collections;

namespace LibBundle3.Nodes;
/// <summary>
/// Do not implement this interface, use <see cref="IFileNode"/> and <see cref="IDirectoryNode"/> instead
/// </summary>
public interface ITreeNode {
	/// <summary>
	/// Parent node of this node, or null if this is the Root node
	/// </summary>
	public IDirectoryNode? Parent { get; }
	public string Name { get; }

	/// <summary>
	/// Get the absolute path of the <paramref name="node"/> in the tree, and ends with '/' if this is <see cref="IDirectoryNode"/>
	/// </summary>
	[SkipLocalsInit]
	public static string GetPath(ITreeNode node) {
		if (node is IFileNode fn)
			return fn.Record.Path;

		var builder = new ValueList<char>(stackalloc char[128]);
		try {
			node.GetPath(ref builder);
			return new(builder.AsReadOnlySpan());
		} finally {
			builder.Dispose();
		}
	}
	private void GetPath(scoped ref ValueList<char> builder) {
		if (Parent is null) // Root
			return;
		Parent.GetPath(ref builder);
		builder.AddRange(Name.AsSpan());
		if (this is IDirectoryNode)
			builder.Add('/');
	}

	/// <summary>
	/// Recurse all nodes under <paramref name="node"/> (include self)
	/// </summary>
	public static IEnumerable<ITreeNode> RecurseTree(ITreeNode node) {
		yield return node;
		if (node is IDirectoryNode dr)
			foreach (var n in dr.Children)
				foreach (var nn in RecurseTree(node))
					yield return nn;
	}
}