﻿using System;
using System.Collections.Generic;

namespace Rssdp {

	internal sealed class ReadOnlyEnumerable<T> : System.Collections.Generic.IEnumerable<T> {

		#region Fields

		private readonly IEnumerable<T> _Items;

		#endregion

		#region Constructors

		public ReadOnlyEnumerable( IEnumerable<T> items ) {

			if( items == null )
				throw new ArgumentNullException("items");

			_Items = items;
		}

		#endregion

		#region IEnumerable<T> Members

		public IEnumerator<T> GetEnumerator() {
			return _Items.GetEnumerator();
		}

		#endregion

		#region IEnumerable Members

		System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() {
			return _Items.GetEnumerator();
		}

		#endregion
	}
}