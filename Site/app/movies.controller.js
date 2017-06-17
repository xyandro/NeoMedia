(function () {
	angular.module('app').controller('MoviesController', MoviesController);

	MoviesController.$inject = ['$http', '$filter'];
	function MoviesController($http, $filter) {
		var vm = this;
		vm.searchText = '';
		vm.curPos = 0;

		vm.refresh = function () {
			$http.get('service/movies').then(function (response) {
				vm.movies = response.data;
				setTimeout(vm.refresh, 5000);
			});
		}

		vm.resetSearch = function (movie) {
			vm.searchText = '';
		}

		vm.queueMovie = function (movie) {
			var url = 'service/' + (movie.queued ? "de" : "en") + 'queue?movie=' + encodeURIComponent(movie.name);
			$http.get(url).then(function (response) {
				movie.queued = !movie.queued;
			});
		}

		vm.queueMovies = function () {
			var enqueue = false;
			var result = $filter('filter')(vm.movies, vm.searchText);
			var str = "";
			for (var x = 0; x < result.length; ++x) {
				str += x == 0 ? "?" : "&";
				str += 'movie=' + encodeURIComponent(result[x].name);
				if (!result[x].queued)
					enqueue = true;
			}
			var url = 'service/' + (enqueue ? "en" : "de") + 'queue' + str;
			$http.get(url).then(function (response) {
				for (var x = 0; x < result.length; ++x) {
					result[x].queued = enqueue;
				}
			});
		}

		vm.pause = function () {
			$http.get('service/pause');
		}

		vm.next = function () {
			$http.get('service/next');
		}

		vm.setPos = function (value) {
			$http.get('service/setpos?pos=' + value);
		}

		vm.jumpPos = function (value) {
			$http.get('service/jumppos?offset=' + value);
		}

		vm.updatePosition = function () {
			$http.get('service/getpos').then(function (response) {
				vm.test = true;
				vm.curPos = response.data;
			});
		}

		vm.refresh();
		setInterval(vm.updatePosition, 1000);
	}
})();